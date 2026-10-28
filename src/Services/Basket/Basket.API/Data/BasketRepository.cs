using System.Collections.Concurrent;
using Basket.API.Models;

namespace Basket.API.Data;

public interface IBasketRepository
{
    ShoppingCart Get(string userName);
    ShoppingCart Save(ShoppingCart cart);
    void Delete(string userName);
}

/// <summary>
/// In-memory basket store. In production this is a natural fit for Redis
/// (key = user name, value = serialized cart, sliding expiry).
/// </summary>
public sealed class InMemoryBasketRepository : IBasketRepository
{
    private readonly ConcurrentDictionary<string, ShoppingCart> _carts = new(StringComparer.OrdinalIgnoreCase);

    public ShoppingCart Get(string userName) => _carts.GetValueOrDefault(userName) ?? ShoppingCart.Empty(userName);

    public ShoppingCart Save(ShoppingCart cart) => _carts[cart.UserName] = cart;

    public void Delete(string userName) => _carts.TryRemove(userName, out _);
}
