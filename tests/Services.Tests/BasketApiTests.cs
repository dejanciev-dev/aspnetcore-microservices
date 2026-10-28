using System.Net;
using System.Net.Http.Json;
using Basket.API;
using Basket.API.Clients;
using Basket.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Tests;

public sealed class BasketApiTests(BasketApiTests.Factory factory) : IClassFixture<BasketApiTests.Factory>
{
    private static readonly CatalogProduct Keyboard = new(Guid.NewGuid(), "Keyboard", "", 129.99m);
    private static readonly ShippingAddress Address = new("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient ClientFor(string user) => factory.CreateClient().As(user, "customer");
    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    [Fact]
    public async Task The_basket_requires_a_signed_in_user()
    {
        var response = await factory.CreateClient().GetAsync("/api/basket", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Adding_an_item_uses_the_catalog_price()
    {
        var client = ClientFor(NewUser());

        await client.PostAsJsonAsync("/api/basket/items", new AddItemRequest(Keyboard.Id, 2), Ct);
        var cart = await client.GetFromJsonAsync<ShoppingCart>("/api/basket", Ct);

        var item = Assert.Single(cart!.Items);
        Assert.Equal(Keyboard.Price, item.UnitPrice);
        Assert.Equal(2 * Keyboard.Price, cart.Total);
    }

    [Fact]
    public async Task Each_user_only_sees_their_own_basket()
    {
        var ada = ClientFor(NewUser());
        var grace = ClientFor(NewUser());

        await ada.PostAsJsonAsync("/api/basket/items", new AddItemRequest(Keyboard.Id), Ct);
        var graceCart = await grace.GetFromJsonAsync<ShoppingCart>("/api/basket", Ct);

        Assert.Empty(graceCart!.Items);
    }

    [Fact]
    public async Task Adding_an_unknown_product_returns_400()
    {
        var response = await ClientFor(NewUser()).PostAsJsonAsync("/api/basket/items", new AddItemRequest(Guid.NewGuid()), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_creates_an_order_for_the_signed_in_user_and_clears_the_basket()
    {
        var user = NewUser();
        var client = ClientFor(user);
        await client.PostAsJsonAsync("/api/basket/items", new AddItemRequest(Keyboard.Id), Ct);

        var response = await client.PostAsJsonAsync("/api/basket/checkout", new CheckoutRequest(Address), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(factory.Ordering.Submitted, c => c.UserName == user);
        Assert.Empty((await client.GetFromJsonAsync<ShoppingCart>("/api/basket", Ct))!.Items);
    }

    [Fact]
    public async Task Checkout_of_empty_basket_returns_400()
    {
        var response = await ClientFor(NewUser()).PostAsJsonAsync("/api/basket/checkout", new CheckoutRequest(Address), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<BasketApiMarker>
    {
        public FakeOrderingClient Ordering { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
                services.AddTestAuthentication();
                services.AddSingleton<ICatalogClient>(new FakeCatalogClient(Keyboard));
                services.AddSingleton<IOrderingClient>(Ordering);
            });
    }

    private sealed class FakeCatalogClient(params CatalogProduct[] products) : ICatalogClient
    {
        public Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(products.FirstOrDefault(p => p.Id == id));
    }

    public sealed class FakeOrderingClient : IOrderingClient
    {
        public List<ShoppingCart> Submitted { get; } = [];

        public Task<CheckoutResult> CreateOrderAsync(ShoppingCart cart, ShippingAddress address, CancellationToken ct = default)
        {
            lock (Submitted) Submitted.Add(cart);
            return Task.FromResult(new CheckoutResult(Guid.NewGuid(), cart.Total));
        }
    }
}
