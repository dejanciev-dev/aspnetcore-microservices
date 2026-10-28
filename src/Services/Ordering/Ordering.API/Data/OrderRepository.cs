using System.Collections.Concurrent;
using Ordering.API.Models;

namespace Ordering.API.Data;

public interface IOrderRepository
{
    Task<IReadOnlyList<Order>> ListByUserAsync(string userName, CancellationToken ct = default);
    Task<Order?> GetAsync(Guid id, CancellationToken ct = default);
    /// <summary>Adds the order unless one with the same id exists (idempotent message handling).</summary>
    Task<bool> TryAddAsync(Order order, CancellationToken ct = default);
}

/// <summary>In-memory store used when no database is configured.</summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task<IReadOnlyList<Order>> ListByUserAsync(string userName, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Order>>(_orders.Values
            .Where(o => o.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.CreatedAt)
            .ToList());

    public Task<Order?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_orders.GetValueOrDefault(id));

    public Task<bool> TryAddAsync(Order order, CancellationToken ct = default) => Task.FromResult(_orders.TryAdd(order.Id, order));
}
