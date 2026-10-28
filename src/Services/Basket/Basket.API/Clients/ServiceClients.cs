using System.Net;
using Basket.API.Models;

namespace Basket.API.Clients;

public sealed record CatalogProduct(Guid Id, string Name, string ImageUrl, decimal Price);

public interface ICatalogClient
{
    Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default);
}

public interface IOrderingClient
{
    Task<CheckoutResult> CreateOrderAsync(ShoppingCart cart, ShippingAddress address, CancellationToken ct = default);
}

/// <summary>Typed HttpClient for Catalog.API — the source of truth for names and prices.</summary>
public sealed class CatalogClient(HttpClient http) : ICatalogClient
{
    public async Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"/api/products/{id}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CatalogProduct>(ct);
    }
}

/// <summary>Typed HttpClient for Ordering.API.</summary>
public sealed class OrderingClient(HttpClient http) : IOrderingClient
{
    private sealed record OrderLine(Guid ProductId, string ProductName, decimal UnitPrice, int Quantity);
    private sealed record CreateOrder(string UserName, IReadOnlyList<OrderLine> Items, ShippingAddress ShippingAddress);
    private sealed record CreatedOrder(Guid Id, decimal Total);

    public async Task<CheckoutResult> CreateOrderAsync(ShoppingCart cart, ShippingAddress address, CancellationToken ct = default)
    {
        var request = new CreateOrder(
            cart.UserName,
            cart.Items.Select(i => new OrderLine(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity)).ToList(),
            address);

        using var response = await http.PostAsJsonAsync("/api/orders", request, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreatedOrder>(ct)
                      ?? throw new InvalidOperationException("Ordering service returned an empty response.");
        return new CheckoutResult(created.Id, created.Total);
    }
}
