using System.Net;
using Basket.API.Clients;
using Basket.API.Data;
using Basket.API.Events;
using Basket.API.Models;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PactNet;
using PactNet.Matchers;

namespace Services.ContractTests.Consumers;

/// <summary>
/// What Basket.API needs from Catalog and Ordering, written down by Basket. Each test runs Basket's
/// real client or handler against a Pact mock; only the fields Basket reads are in the contract, so
/// providers stay free to add fields.
/// </summary>
[Collection(PactCollection.Name)]
public sealed class BasketConsumerTests
{
    // Fixed values keep the generated pact files stable between runs.
    internal static readonly Guid ExistingProductId = Guid.Parse("6f1c0a52-3c7e-4a8e-9d0b-2b6f6d1f4a11");
    internal static readonly Guid MissingProductId = Guid.Parse("00000000-0000-4000-8000-000000000404");
    private const string GuidPattern = "^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Catalog_returns_name_image_and_price_of_a_product()
    {
        var pact = Pact.V4("Basket.API", "Catalog.API", Pacts.Config).WithHttpInteractions();
        pact.UponReceiving("a request for a product")
            .Given("a product exists", new Dictionary<string, string> { ["id"] = ExistingProductId.ToString() })
            .WithRequest(HttpMethod.Get, $"/api/products/{ExistingProductId}")
            .WillRespond()
            .WithStatus(HttpStatusCode.OK)
            .WithJsonBody(new
            {
                id = ExistingProductId,
                name = Match.Type("Mechanical keyboard"),
                imageUrl = Match.Type("https://example.com/keyboard.png"),
                price = Match.Number(89.99m),
            });

        await pact.VerifyAsync(async server =>
        {
            var catalog = new CatalogClient(new HttpClient { BaseAddress = server.MockServerUri });

            var product = await catalog.GetProductAsync(ExistingProductId, Ct);

            Assert.Equal(new CatalogProduct(ExistingProductId, "Mechanical keyboard", "https://example.com/keyboard.png", 89.99m), product);
        });
    }

    [Fact]
    public async Task Catalog_answers_404_for_an_unknown_product()
    {
        var pact = Pact.V4("Basket.API", "Catalog.API", Pacts.Config).WithHttpInteractions();
        pact.UponReceiving("a request for a product that does not exist")
            .Given("no product exists", new Dictionary<string, string> { ["id"] = MissingProductId.ToString() })
            .WithRequest(HttpMethod.Get, $"/api/products/{MissingProductId}")
            .WillRespond()
            .WithStatus(HttpStatusCode.NotFound);

        await pact.VerifyAsync(async server =>
        {
            var catalog = new CatalogClient(new HttpClient { BaseAddress = server.MockServerUri });

            Assert.Null(await catalog.GetProductAsync(MissingProductId, Ct));
        });
    }

    [Fact]
    public async Task Ordering_creates_an_order_for_the_relayed_user()
    {
        var pact = Pact.V4("Basket.API", "Ordering.API", Pacts.Config).WithHttpInteractions();
        pact.UponReceiving("a request to create an order")
            .WithRequest(HttpMethod.Post, "/api/orders")
            .WithHeader("Authorization", Match.Regex("Bearer token-of-the-signed-in-user", "^Bearer .+$"))
            .WithJsonBody(new
            {
                items = new[]
                {
                    new { productId = ExistingProductId, productName = "Mechanical keyboard", unitPrice = 89.99m, quantity = 2 },
                },
                shippingAddress = new { fullName = "Ada Lovelace", street = "12 Analytical St", city = "London", country = "UK", zipCode = "N1 9GU" },
            })
            .WillRespond()
            .WithStatus(HttpStatusCode.Created)
            .WithJsonBody(new
            {
                id = Match.Regex("3fa85f64-5717-4562-b3fc-2c963f66afa6", GuidPattern),
                total = Match.Number(179.98m),
            });

        await pact.VerifyAsync(async server =>
        {
            // Basket's real token relay: the incoming request's bearer token goes to Ordering.
            var incoming = new DefaultHttpContext();
            incoming.Request.Headers.Authorization = "Bearer token-of-the-signed-in-user";
            var relay = new ForwardAccessTokenHandler(new HttpContextAccessor { HttpContext = incoming }) { InnerHandler = new HttpClientHandler() };
            var ordering = new OrderingClient(new HttpClient(relay) { BaseAddress = server.MockServerUri });

            var cart = new ShoppingCart("ada", [new CartItem(ExistingProductId, "Mechanical keyboard", "https://example.com/keyboard.png", 89.99m, 2)]);
            var address = new ShippingAddress("Ada Lovelace", "12 Analytical St", "London", "UK", "N1 9GU");

            var result = await ordering.CreateOrderAsync(cart, address, Ct);

            Assert.Equal(Guid.Parse("3fa85f64-5717-4562-b3fc-2c963f66afa6"), result.OrderId);
            Assert.Equal(179.98m, result.Total);
        });
    }

    [Fact]
    public async Task Catalog_price_change_events_reprice_open_baskets()
    {
        var pact = Pact.V3("Basket.API", "Catalog.API.Events", Pacts.Config).WithMessageInteractions();
        var baskets = new InMemoryBasketRepository();
        await baskets.SaveAsync(new ShoppingCart("ada", [new CartItem(ExistingProductId, "Mechanical keyboard", "", 89.99m, 1)]), Ct);

        await pact.ExpectsToReceive("catalog.product-price-changed")
            .WithJsonContent(new
            {
                id = Match.Regex("0d9e4c1a-8b7f-4f3e-9a51-6c2d8e7f1b20", GuidPattern),
                occurredAt = Match.Type("2026-10-29T10:15:30.1234567+00:00"),
                productId = Match.Regex(ExistingProductId.ToString(), GuidPattern),
                newPrice = Match.Number(79.99m),
                oldPrice = Match.Number(89.99m),
            })
            .VerifyAsync<ProductPriceChanged>(async @event =>
                await new ProductPriceChangedHandler(baskets, NullLogger<ProductPriceChangedHandler>.Instance).HandleAsync(@event, Ct));

        Assert.Equal(79.99m, (await baskets.GetAsync("ada", Ct)).Items.Single().UnitPrice);
    }
}
