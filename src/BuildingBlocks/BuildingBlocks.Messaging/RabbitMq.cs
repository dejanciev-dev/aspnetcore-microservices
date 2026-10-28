using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;

namespace BuildingBlocks.Messaging;

/// <summary>Broker topology shared by every service.</summary>
public static class RabbitMqTopology
{
    /// <summary>Durable topic exchange all integration events are published to.</summary>
    public const string Exchange = "storefront.events";

    /// <summary>Messages that keep failing are dead-lettered here, then routed to "&lt;queue&gt;.dlq".</summary>
    public const string DeadLetterExchange = "storefront.events.dlx";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static Task DeclareExchangesAsync(IChannel channel, CancellationToken ct) => Task.WhenAll(
        channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct),
        channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct));
}

/// <summary>
/// Tracing for messages: producer/consumer spans plus W3C trace-context in the message headers,
/// so one trace follows a request through the broker into the consuming service.
/// </summary>
internal static class MessagingTelemetry
{
    public const string ActivitySourceName = "BuildingBlocks.Messaging";
    public static readonly ActivitySource Source = new(ActivitySourceName);

    private static TextMapPropagator Propagator => Propagators.DefaultTextMapPropagator;

    public static void Inject(Activity? activity, IDictionary<string, object?> headers)
    {
        if (activity is null) return;
        Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), headers,
            static (carrier, key, value) => carrier[key] = value);
    }

    public static ActivityContext Extract(IDictionary<string, object?>? headers) =>
        headers is null
            ? default
            : Propagator.Extract(default, headers, static (carrier, key) =>
                carrier.TryGetValue(key, out var value) && value is byte[] bytes ? [Encoding.UTF8.GetString(bytes)]
                : carrier.TryGetValue(key, out value) && value is string text ? [text]
                : []).ActivityContext;

    public static void Tag(Activity? activity, string routingKey, Guid messageId)
    {
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination.name", RabbitMqTopology.Exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", messageId.ToString());
    }
}

/// <summary>
/// Publishes to the topic exchange on a single channel with publisher confirms, so
/// <see cref="PublishAsync{TEvent}"/> only completes once the broker has the message.
/// </summary>
internal sealed class RabbitMqPublisher(IConnection connection) : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IntegrationEvent =>
        PublishRawAsync(EventNames.Of(typeof(TEvent)), @event.Id, JsonSerializer.SerializeToUtf8Bytes(@event, RabbitMqTopology.Json), parent: default, ct);

    /// <summary>Used by the outbox relay, which already holds the serialized payload.</summary>
    internal async Task PublishRawAsync(string routingKey, Guid messageId, ReadOnlyMemory<byte> body, ActivityContext parent, CancellationToken ct)
    {
        using var activity = MessagingTelemetry.Source.StartActivity($"publish {routingKey}", ActivityKind.Producer, parent);
        MessagingTelemetry.Tag(activity, routingKey, messageId);

        var properties = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Type = routingKey,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>(),
        };
        MessagingTelemetry.Inject(activity, properties.Headers);

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            // With confirmation tracking enabled this awaits the broker ack and throws PublishException on a nack.
            await channel.BasicPublishAsync(RabbitMqTopology.Exchange, routingKey, mandatory: false, properties, body, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;

        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await RabbitMqTopology.DeclareExchangesAsync(_channel, ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _lock.Dispose();
    }
}
