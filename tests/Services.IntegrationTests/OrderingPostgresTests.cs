using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Ordering.API;
using Ordering.API.Models;

namespace Services.IntegrationTests;

public sealed class OrderingPostgresTests(Infrastructure infra)
{
    private static readonly ShippingAddress Address = new("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");

    private WebApplicationFactory<OrderingApiMarker> CreateApp(string database) =>
        new WebApplicationFactory<OrderingApiMarker>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:orderingdb", infra.PostgresConnectionString(database))
            .ConfigureTestServices(s => s.AddTestAuthentication()));

    [Fact]
    public async Task Orders_with_lines_and_address_survive_a_restart()
    {
        var database = $"ordering_{Guid.NewGuid():N}";
        var ct = TestContext.Current.CancellationToken;
        Order created;

        await using (var app = CreateApp(database))
        {
            var response = await app.CreateClient().As("ada").PostAsJsonAsync("/api/orders", new CreateOrderRequest(
            [
                new OrderItem(Guid.NewGuid(), "Keyboard", 100m, 2),
                new OrderItem(Guid.NewGuid(), "Mouse", 25.50m, 1)
            ], Address), ct);

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            created = (await response.Content.ReadFromJsonAsync<Order>(ct))!;
        }

        await using (var app = CreateApp(database))
        {
            var order = await app.CreateClient().As("ada").GetFromJsonAsync<Order>($"/api/orders/{created.Id}", ct);

            Assert.NotNull(order);
            Assert.Equal(225.50m, order.Total);
            Assert.Equal(OrderStatus.Submitted, order.Status);
            Assert.Equal(Address, order.ShippingAddress);
            Assert.Equal(["Keyboard", "Mouse"], order.Items.Select(i => i.ProductName));
            // timestamptz keeps microseconds; .NET ticks are 100ns.
            Assert.Equal(created.CreatedAt, order.CreatedAt, TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task Listing_is_per_user_case_insensitive_and_newest_first()
    {
        await using var app = CreateApp($"ordering_{Guid.NewGuid():N}");
        var ct = TestContext.Current.CancellationToken;
        var request = new CreateOrderRequest([new(Guid.NewGuid(), "Keyboard", 10m, 1)], Address);

        var first = await (await app.CreateClient().As("Grace").PostAsJsonAsync("/api/orders", request, ct)).Content.ReadFromJsonAsync<Order>(ct);
        var second = await (await app.CreateClient().As("grace").PostAsJsonAsync("/api/orders", request, ct)).Content.ReadFromJsonAsync<Order>(ct);
        await app.CreateClient().As("someone-else").PostAsJsonAsync("/api/orders", request, ct);

        var orders = await app.CreateClient().As("GRACE").GetFromJsonAsync<List<Order>>("/api/orders", ct);

        Assert.Equal([second!.Id, first!.Id], orders!.Select(o => o.Id));
    }

    [Fact]
    public async Task The_same_product_can_appear_on_several_lines()
    {
        await using var app = CreateApp($"ordering_{Guid.NewGuid():N}");
        var productId = Guid.NewGuid();

        var response = await app.CreateClient().As("ada").PostAsJsonAsync("/api/orders", new CreateOrderRequest(
        [
            new OrderItem(productId, "Keyboard", 100m, 1),
            new OrderItem(productId, "Keyboard", 100m, 1)
        ], Address), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
