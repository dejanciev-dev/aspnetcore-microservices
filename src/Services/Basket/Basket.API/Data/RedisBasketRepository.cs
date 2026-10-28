using System.Text.Json;
using Basket.API.Models;
using StackExchange.Redis;

namespace Basket.API.Data;

/// <summary>
/// Redis-backed basket store: one JSON document per user under <c>basket:{user}</c>,
/// with a sliding 30-day expiry that is renewed on every write.
/// </summary>
public sealed class RedisBasketRepository(IConnectionMultiplexer redis) : IBasketRepository
{
    private static readonly TimeSpan Expiry = TimeSpan.FromDays(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IDatabase Db => redis.GetDatabase();

    public async Task<ShoppingCart> GetAsync(string userName, CancellationToken ct = default)
    {
        var value = await Db.StringGetAsync(Key(userName));
        return value.IsNullOrEmpty
            ? ShoppingCart.Empty(userName)
            : JsonSerializer.Deserialize<ShoppingCart>(value.ToString(), Json) ?? ShoppingCart.Empty(userName);
    }

    public async Task<ShoppingCart> SaveAsync(ShoppingCart cart, CancellationToken ct = default)
    {
        await Db.StringSetAsync(Key(cart.UserName), JsonSerializer.Serialize(cart, Json), Expiry);
        return cart;
    }

    public Task DeleteAsync(string userName, CancellationToken ct = default) => Db.KeyDeleteAsync(Key(userName));

    // User names are case-insensitive, matching the in-memory store.
    private static RedisKey Key(string userName) => $"basket:{userName.ToLowerInvariant()}";
}
