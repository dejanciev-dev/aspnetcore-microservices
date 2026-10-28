using System.Collections.Concurrent;
using BuildingBlocks.Common;
using Catalog.API.Models;

namespace Catalog.API.Data;

public interface IProductRepository
{
    PagedResult<Product> List(string? category, string? search, int page, int pageSize);
    IReadOnlyList<string> Categories();
    Product? Get(Guid id);
    Product Add(Product product);
    bool Update(Product product);
    bool Delete(Guid id);
}

/// <summary>
/// Thread-safe in-memory store so the demo runs with zero infrastructure.
/// Swap for an EF Core / MongoDB implementation without touching the endpoints.
/// </summary>
public sealed class InMemoryProductRepository : IProductRepository
{
    private readonly ConcurrentDictionary<Guid, Product> _products = new(
        SeedData.Products.ToDictionary(p => p.Id));

    public PagedResult<Product> List(string? category, string? search, int page, int pageSize)
    {
        IEnumerable<Product> query = _products.Values;

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                  || p.Description.Contains(search, StringComparison.OrdinalIgnoreCase));

        var filtered = query.OrderBy(p => p.Name).ToList();
        var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return new PagedResult<Product>(items, page, pageSize, filtered.Count);
    }

    public IReadOnlyList<string> Categories() =>
        _products.Values.Select(p => p.Category).Distinct().Order().ToList();

    public Product? Get(Guid id) => _products.GetValueOrDefault(id);

    public Product Add(Product product)
    {
        _products[product.Id] = product;
        return product;
    }

    public bool Update(Product product)
    {
        if (!_products.ContainsKey(product.Id)) return false;
        _products[product.Id] = product;
        return true;
    }

    public bool Delete(Guid id) => _products.TryRemove(id, out _);
}

internal static class SeedData
{
    public static readonly Product[] Products =
    [
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000001"), "Mechanical Keyboard", "Hot-swappable 75% keyboard with tactile switches.", "Peripherals", 129.99m, Img("keyboard")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000002"), "Wireless Mouse", "Ergonomic mouse with 70-day battery life.", "Peripherals", 59.90m, Img("mouse")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000003"), "4K Monitor", "27\" IPS panel, USB-C power delivery.", "Displays", 399.00m, Img("monitor")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000004"), "Ultrawide Monitor", "34\" curved display for multitasking.", "Displays", 549.00m, Img("ultrawide")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000005"), "Noise-Cancelling Headphones", "Over-ear, adaptive ANC, 30h playback.", "Audio", 249.50m, Img("headphones")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000006"), "USB Microphone", "Cardioid condenser mic for streaming and calls.", "Audio", 89.00m, Img("microphone")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000007"), "Laptop Stand", "Aluminium stand with adjustable height.", "Accessories", 39.99m, Img("stand")),
        new(Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000008"), "USB-C Dock", "11-in-1 dock with dual HDMI and 100W PD.", "Accessories", 149.00m, Img("dock")),
    ];

    private static string Img(string seed) => $"https://picsum.photos/seed/{seed}/480/320";
}
