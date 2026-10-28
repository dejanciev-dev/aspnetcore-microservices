using System.Collections.Concurrent;
using BuildingBlocks.Common;
using Catalog.API.Models;

namespace Catalog.API.Data;

public interface IProductRepository
{
    Task<PagedResult<Product>> ListAsync(string? category, string? search, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default);
    Task<Product?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Product> AddAsync(Product product, CancellationToken ct = default);
    Task<bool> UpdateAsync(Product product, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Thread-safe in-memory store used when no database is configured, so the demo
/// (and the fast test suite) runs with zero infrastructure.
/// </summary>
public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<Guid, Product> _products = new(
        SeedData.Products.ToDictionary(p => p.Id));

    public Task<PagedResult<Product>> ListAsync(string? category, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        IEnumerable<Product> query = _products.Values;

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || p.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        var filtered = query.OrderBy(p => p.Name).ToList();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(new PagedResult<Product>(items, page, pageSize, filtered.Count));
    }

    public Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(_products.Values.Select(p => p.Category).Distinct().Order().ToList());

    public Task<Product?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_products.GetValueOrDefault(id));

    public Task<Product> AddAsync(Product product, CancellationToken ct = default)
    {
        _products[product.Id] = product;
        return Task.FromResult(product);
    }

    public Task<bool> UpdateAsync(Product product, CancellationToken ct = default)
    {
        if (!_products.ContainsKey(product.Id)) return Task.FromResult(false);
        _products[product.Id] = product;
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => Task.FromResult(_products.TryRemove(id, out _));
}
