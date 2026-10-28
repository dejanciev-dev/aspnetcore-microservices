using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Messaging;

public static class MessagingExtensions
{
    public const string ConnectionName = "rabbitmq";

    /// <summary>
    /// Connects to RabbitMQ if <c>ConnectionStrings:rabbitmq</c> is configured (Aspire and
    /// docker-compose set it) and registers the publisher and the consumer host.
    /// Returns <c>false</c> when no broker is configured, so callers can fall back.
    /// </summary>
    /// <param name="queuePrefix">This service's queue prefix, e.g. "basket-api".</param>
    public static bool AddRabbitMqMessaging(this IHostApplicationBuilder builder, string queuePrefix)
    {
        if (builder.Configuration.GetConnectionString(ConnectionName) is null) return false;

        // Aspire integration: shared IConnection with automatic recovery, health check and tracing.
        builder.AddRabbitMQClient(ConnectionName);

        builder.Services.AddSingleton(new MessagingOptions { QueuePrefix = queuePrefix });
        builder.Services.AddSingleton<RabbitMqPublisher>();
        builder.Services.AddSingleton<IEventPublisher>(sp => sp.GetRequiredService<RabbitMqPublisher>());
        builder.Services.AddHostedService<RabbitMqConsumer>();
        return true;
    }

    /// <summary>Subscribes this service to <typeparamref name="TEvent"/>.</summary>
    public static IServiceCollection AddEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IntegrationEvent
        where THandler : class, IIntegrationEventHandler<TEvent>
    {
        var options = services.LastOrDefault(d => d.ServiceType == typeof(MessagingOptions))?.ImplementationInstance as MessagingOptions
                      ?? throw new InvalidOperationException("Call AddRabbitMqMessaging() before AddEventHandler().");

        services.AddScoped<IIntegrationEventHandler<TEvent>, THandler>();
        options.Subscriptions.Add(new Subscription(
            EventNames.Of(typeof(TEvent)),
            typeof(TEvent),
            (sp, @event, ct) => sp.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync((TEvent)@event, ct)));
        return services;
    }

    /// <summary>Publishes messages staged with <see cref="OutboxExtensions.AddOutboxMessage{TEvent}"/>.</summary>
    public static IServiceCollection AddOutboxRelay<TDbContext>(this IServiceCollection services) where TDbContext : DbContext =>
        services.AddHostedService<OutboxRelay<TDbContext>>();
}
