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

public sealed class BasketApiTests : IClassFixture<BasketApiTests.Factory>
{
    private static readonly CatalogProduct Keyboard = new(Guid.NewGuid(), "Keyboard", "", 129.99m);

    private readonly HttpClient _client;
    private readonly FakeOrderingClient _ordering;

    public BasketApiTests(Factory factory)
    {
        _client = factory.CreateClient();
        _ordering = factory.Ordering;
    }

    [Fact]
    public async Task Adding_an_item_uses_the_catalog_price()
    {
        var user = NewUser();

        await _client.PostAsJsonAsync($"/api/basket/{user}/items", new AddItemRequest(Keyboard.Id, 2));
        var cart = await _client.GetFromJsonAsync<ShoppingCart>($"/api/basket/{user}");

        var item = Assert.Single(cart!.Items);
        Assert.Equal(Keyboard.Price, item.UnitPrice);
        Assert.Equal(2 * Keyboard.Price, cart.Total);
    }

    [Fact]
    public async Task Adding_an_unknown_product_returns_400()
    {
        var response = await _client.PostAsJsonAsync($"/api/basket/{NewUser()}/items", new AddItemRequest(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_creates_an_order_and_clears_the_basket()
    {
        var user = NewUser();
        await _client.PostAsJsonAsync($"/api/basket/{user}/items", new AddItemRequest(Keyboard.Id));

        var address = new ShippingAddress("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");
        var response = await _client.PostAsJsonAsync($"/api/basket/{user}/checkout", new CheckoutRequest(address));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_ordering.Submitted, c => c.UserName == user);

        var cart = await _client.GetFromJsonAsync<ShoppingCart>($"/api/basket/{user}");
        Assert.Empty(cart!.Items);
    }

    [Fact]
    public async Task Checkout_of_empty_basket_returns_400()
    {
        var address = new ShippingAddress("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");

        var response = await _client.PostAsJsonAsync($"/api/basket/{NewUser()}/checkout", new CheckoutRequest(address));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static string NewUser() => $"user-{Guid.NewGuid():N}";

    public sealed class Factory : WebApplicationFactory<BasketApiMarker>
    {
        public FakeOrderingClient Ordering { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services =>
            {
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
