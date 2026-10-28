using Microsoft.EntityFrameworkCore;
using Ordering.API.Models;

namespace Ordering.API.Data;

/// <summary>PostgreSQL-backed store (EF Core + Npgsql).</summary>
public sealed class EfOrderRepository(OrderingDbContext db) : IOrderRepository
{
    public async Task<IReadOnlyList<Order>> ListByUserAsync(string userName, CancellationToken ct = default)
    {
        var entities = await db.Orders.AsNoTracking()
            .Where(o => o.UserName.ToLower() == userName.ToLower())
            .OrderByDescending(o => o.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);

        return entities.Select(e => e.ToOrder()).ToList();
    }

    public async Task<Order?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct))?.ToOrder();

    public async Task<Order> AddAsync(Order order, CancellationToken ct = default)
    {
        db.Orders.Add(OrderEntity.From(order));
        await db.SaveChangesAsync(ct);
        return order;
    }
}
