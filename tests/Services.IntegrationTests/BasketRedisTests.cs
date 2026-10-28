using System.Net;
using System.Net.Http.Json;
using Basket.API;
using Basket.API.Clients;
using Basket.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Services.IntegrationTests;

public sealed class BasketRedisTests(Infrastructure infra)
{
    private static readonly CatalogProduct Keyboard = new(Guid.NewGuid(), "Keyboard", "", 129.99m);
    private static readonly ShippingAddress Address = new("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");

    // Catalog and Ordering are faked; only Redis is real.
    private WebApplicationFactory<BasketApiMarker> CreateApp() =>
        new WebApplicationFactory<BasketApiMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:redis", infra.RedisConnectionString);
            b.ConfigureTestServices(services =>
            {
                services.AddTestAuthentication();
                services.AddSingleton<ICatalogClient>(new FakeCatalog());
                services.AddSingleton<IOrderingClient>(new FakeOrdering());
            });
        });

    [Fact]
    public async Task Basket_is_stored_in_redis_with_an_expiry_and_survives_a_restart()
    {
        var user = $"User-{Guid.NewGuid():N}";
        var ct = TestContext.Current.CancellationToken;

        await using (var app = CreateApp())
        {
            var response = await app.CreateClient().As(user).PostAsJsonAsync("/api/basket/items", new AddItemRequest(Keyboard.Id, 2), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var redis = app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
            var ttl = await redis.KeyTimeToLiveAsync($"basket:{user.ToLowerInvariant()}");
            Assert.InRange(ttl!.Value, TimeSpan.FromDays(29), TimeSpan.FromDays(30));
        }

        await using (var app = CreateApp())
        {
            // Lookup is case-insensitive, like the in-memory store.
            var cart = await app.CreateClient().As(user.ToUpperInvariant()).GetFromJsonAsync<ShoppingCart>("/api/basket", ct);

            var item = Assert.Single(cart!.Items);
            Assert.Equal(2, item.Quantity);
            Assert.Equal(2 * Keyboard.Price, cart.Total);
        }
    }

    [Fact]
    public async Task Checkout_removes_the_basket_from_redis()
    {
        var user = $"user-{Guid.NewGuid():N}";
        var ct = TestContext.Current.CancellationToken;
        await using var app = CreateApp();
        var client = app.CreateClient().As(user);

        await client.PostAsJsonAsync("/api/basket/items", new AddItemRequest(Keyboard.Id), ct);
        var checkout = await client.PostAsJsonAsync("/api/basket/checkout", new CheckoutRequest(Address), ct);

        Assert.Equal(HttpStatusCode.OK, checkout.StatusCode);
        var redis = app.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync($"basket:{user}"));
    }

    private sealed class FakeCatalog : ICatalogClient
    {
        public Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(id == Keyboard.Id ? Keyboard : null);
    }

    private sealed class FakeOrdering : IOrderingClient
    {
        public Task<CheckoutResult> CreateOrderAsync(ShoppingCart cart, ShippingAddress address, CancellationToken ct = default) =>
            Task.FromResult(new CheckoutResult(Guid.NewGuid(), cart.Total));
    }
}
