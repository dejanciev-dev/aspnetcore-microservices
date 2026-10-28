using Microsoft.EntityFrameworkCore;
using Npgsql;
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

    public async Task<bool> TryAddAsync(Order order, CancellationToken ct = default)
    {
        if (await db.Orders.AnyAsync(o => o.Id == order.Id, ct)) return false;

        db.Orders.Add(OrderEntity.From(order));
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;   // a concurrent delivery of the same message won the race
        }
    }
}
