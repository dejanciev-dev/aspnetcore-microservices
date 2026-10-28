using System.Collections.Concurrent;
using System.Reflection;

namespace BuildingBlocks.Messaging;

/// <summary>
/// Base type for events that cross service boundaries. Each service declares its own copy of
/// the events it publishes or consumes; only the JSON shape and the <see cref="EventNameAttribute"/>
/// have to match.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>The stable, versionable name of an event; used as the RabbitMQ routing key.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EventNameAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Handles one event type. Resolved from a fresh DI scope per message.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

public interface IEventPublisher
{
    /// <summary>Publishes and waits for the broker's confirmation; throws if the broker rejects it.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IntegrationEvent;
}

internal static class EventNames
{
    private static readonly ConcurrentDictionary<Type, string> Cache = new();

    public static string Of(Type type) => Cache.GetOrAdd(type, t =>
        t.GetCustomAttribute<EventNameAttribute>()?.Name
        ?? throw new InvalidOperationException($"{t.Name} needs an [EventName(\"...\")] attribute."));
}
