using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using Catalog.API.Events;
using Catalog.API.Models;
using Microsoft.EntityFrameworkCore;

namespace Catalog.API.Data;

/// <summary>PostgreSQL-backed store (EF Core + Npgsql).</summary>
public sealed class EfProductRepository(CatalogDbContext db) : IProductRepository
{
    public async Task<PagedResult<Product>> ListAsync(string? category, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category.ToLower() == category.ToLower());

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{EscapeLike(search)}%";
            query = query.Where(p => EF.Functions.ILike(p.Name, pattern) || EF.Functions.ILike(p.Description, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return new PagedResult<Product>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default) =>
        await db.Products.Select(p => p.Category).Distinct().OrderBy(c => c).ToListAsync(ct);

    public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return product;
    }

    /// <summary>
    /// Updates the product and, if the price changed, stages a <see cref="ProductPriceChanged"/> event
    /// in the outbox. Both are committed in one transaction, so the event can never be lost or sent
    /// for a change that was rolled back.
    /// </summary>
    public Task<bool> UpdateAsync(Product product, CancellationToken ct = default) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();   // a retried attempt must not re-add the previous attempt's outbox row
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var oldPrice = await db.Products
                .FromSql($"SELECT * FROM products WHERE \"Id\" = {product.Id} FOR UPDATE")
                .Select(p => (decimal?)p.Price)
                .FirstOrDefaultAsync(ct);
            if (oldPrice is null) return false;

            await db.Products
                .Where(p => p.Id == product.Id)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(p => p.Name, product.Name)
                    .SetProperty(p => p.Description, product.Description)
                    .SetProperty(p => p.Category, product.Category)
                    .SetProperty(p => p.Price, product.Price)
                    .SetProperty(p => p.ImageUrl, product.ImageUrl), ct);

            if (oldPrice != product.Price)
            {
                db.AddOutboxMessage(new ProductPriceChanged(product.Id, product.Price, oldPrice.Value));
                await db.SaveChangesAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return true;
        });

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) =>
        await db.Products.Where(p => p.Id == id).ExecuteDeleteAsync(ct) > 0;

    // ILIKE treats % and _ as wildcards; escape them so user input is matched literally.
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
