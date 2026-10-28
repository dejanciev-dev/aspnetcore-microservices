using System.Collections.Concurrent;
using Basket.API.Models;

namespace Basket.API.Data;

public interface IBasketRepository
{
    Task<ShoppingCart> GetAsync(string userName, CancellationToken ct = default);
    Task<ShoppingCart> SaveAsync(ShoppingCart cart, CancellationToken ct = default);
    Task DeleteAsync(string userName, CancellationToken ct = default);

    /// <summary>Applies a new price to every basket containing the product; returns how many changed.</summary>
    Task<int> UpdatePriceAsync(Guid productId, decimal newPrice, CancellationToken ct = default);
}

/// <summary>In-memory basket store used when no Redis connection is configured.</summary>
public sealed class InMemoryBasketRepository : IBasketRepository
{
    private readonly ConcurrentDictionary<string, ShoppingCart> _carts = new(StringComparer.OrdinalIgnoreCase);

    public Task<ShoppingCart> GetAsync(string userName, CancellationToken ct = default) =>
        Task.FromResult(_carts.GetValueOrDefault(userName) ?? ShoppingCart.Empty(userName));

    public Task<ShoppingCart> SaveAsync(ShoppingCart cart, CancellationToken ct = default) =>
        Task.FromResult(_carts[cart.UserName] = cart);

    public Task DeleteAsync(string userName, CancellationToken ct = default)
    {
        _carts.TryRemove(userName, out _);
        return Task.CompletedTask;
    }

    public Task<int> UpdatePriceAsync(Guid productId, decimal newPrice, CancellationToken ct = default)
    {
        var updated = 0;
        foreach (var (user, cart) in _carts)
        {
            if (cart.WithPrice(productId, newPrice) is { } repriced && _carts.TryUpdate(user, repriced, cart))
                updated++;
        }
        return Task.FromResult(updated);
    }
}
