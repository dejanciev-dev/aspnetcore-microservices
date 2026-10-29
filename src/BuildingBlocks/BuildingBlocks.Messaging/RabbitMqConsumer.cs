using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BuildingBlocks.Messaging;

internal sealed record Subscription(string RoutingKey, Type EventType, Func<IServiceProvider, object, CancellationToken, Task> Invoke);

internal sealed class MessagingOptions
{
    /// <summary>Prefix for this service's queues, e.g. "basket-api" → "basket-api.catalog.product-price-changed".</summary>
    public string QueuePrefix { get; set; } = "";
    public List<Subscription> Subscriptions { get; } = [];
}

/// <summary>
/// Declares one durable queue per subscribed event (with a dead-letter queue), consumes it and
/// dispatches each message to its handler in a new DI scope. Handlers are retried a few times;
/// messages that still fail are dead-lettered instead of blocking the queue.
/// </summary>
internal sealed class RabbitMqConsumer(
    IConnection connection,
    IServiceScopeFactory scopes,
    MessagingOptions options,
    ILogger<RabbitMqConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3)];
    private List<IChannel> _channels = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var subscription in options.Subscriptions)
        {
            var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
            _channels.Add(channel);

            var queue = $"{options.QueuePrefix}.{subscription.RoutingKey}";
            await DeclareQueueAsync(channel, queue, subscription.RoutingKey, stoppingToken);
            await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 16, global: false, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => HandleAsync(channel, queue, subscription, delivery, stoppingToken);
            await channel.BasicConsumeAsync(queue, autoAck: false, consumer, stoppingToken);

            logger.LogInformation("Consuming {RoutingKey} from queue {Queue}", subscription.RoutingKey, queue);
        }
    }

    private static async Task DeclareQueueAsync(IChannel channel, string queue, string routingKey, CancellationToken ct)
    {
        await RabbitMqTopology.DeclareExchangesAsync(channel, ct);

        var deadLetterQueue = $"{queue}.dlq";
        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(deadLetterQueue, RabbitMqTopology.DeadLetterExchange, deadLetterQueue, cancellationToken: ct);

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = RabbitMqTopology.DeadLetterExchange,
                ["x-dead-letter-routing-key"] = deadLetterQueue,
            },
            cancellationToken: ct);
        await channel.QueueBindAsync(queue, RabbitMqTopology.Exchange, routingKey, cancellationToken: ct);
    }

    private async Task HandleAsync(IChannel channel, string queue, Subscription subscription, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var parent = MessagingTelemetry.Extract(delivery.BasicProperties.Headers);
        using var activity = MessagingTelemetry.Source.StartActivity($"process {subscription.RoutingKey}", ActivityKind.Consumer, parent);
        MessagingTelemetry.Tag(activity, subscription.RoutingKey, Guid.TryParse(delivery.BasicProperties.MessageId, out var id) ? id : Guid.Empty);

        object? @event;
        try
        {
            @event = JsonSerializer.Deserialize(delivery.Body.Span, subscription.EventType, RabbitMqTopology.Json);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Dead-lettering unreadable message {MessageId} from {Queue}", delivery.BasicProperties.MessageId, queue);
            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await subscription.Invoke(scope.ServiceProvider, @event!, ct);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, ct);
                return;
            }
            catch (Exception ex) when (attempt < RetryDelays.Length && !ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Handler for {RoutingKey} failed (attempt {Attempt}), retrying", subscription.RoutingKey, attempt + 1);
                await Task.Delay(RetryDelays[attempt], ct);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Dead-lettering message {MessageId} from {Queue} after {Attempts} attempts",
                    delivery.BasicProperties.MessageId, queue, attempt + 1);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
                return;
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        // StopAsync can run twice (the test host and the app both stop on shutdown), so only the first
        // call takes the channels. If the connection is already disposed, its channels went with it.
        foreach (var channel in Interlocked.Exchange(ref _channels, []))
        {
            try { await channel.DisposeAsync(); }
            catch (ObjectDisposedException) { }
        }
    }
}
