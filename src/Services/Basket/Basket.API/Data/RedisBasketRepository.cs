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

    /// <summary>
    /// Walks all baskets with SCAN (non-blocking, cursor based) and reprices matching items,
    /// keeping each basket's remaining expiry. Fine at demo scale; a product → baskets index
    /// would avoid the full scan in production.
    /// </summary>
    public async Task<int> UpdatePriceAsync(Guid productId, decimal newPrice, CancellationToken ct = default)
    {
        var updated = 0;
        foreach (var server in redis.GetServers().Where(s => !s.IsReplica))
        {
            await foreach (var key in server.KeysAsync(Db.Database, pattern: "basket:*").WithCancellation(ct))
            {
                var value = await Db.StringGetAsync(key);
                if (value.IsNullOrEmpty) continue;

                var cart = JsonSerializer.Deserialize<ShoppingCart>(value.ToString(), Json);
                if (cart?.WithPrice(productId, newPrice) is not { } repriced) continue;

                await Db.StringSetAsync(key, JsonSerializer.Serialize(repriced, Json), expiry: null, keepTtl: true);
                updated++;
            }
        }
        return updated;
    }

    // User names are case-insensitive, matching the in-memory store.
    private static RedisKey Key(string userName) => $"basket:{userName.ToLowerInvariant()}";
}
