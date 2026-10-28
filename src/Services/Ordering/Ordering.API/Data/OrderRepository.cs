using System.Collections.Concurrent;
using Ordering.API.Models;

namespace Ordering.API.Data;

public interface IOrderRepository
{
    IReadOnlyList<Order> ListByUser(string userName);
    Order? Get(Guid id);
    Order Add(Order order);
}

public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public IReadOnlyList<Order> ListByUser(string userName) =>
        _orders.Values
            .Where(o => o.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(o => o.CreatedAt)
            .ToList();

    public Order? Get(Guid id) => _orders.GetValueOrDefault(id);

    public Order Add(Order order)
    {
        _orders[order.Id] = order;
        return order;
    }
}
