using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Ordering.API;
using Ordering.API.Models;

namespace Services.Tests;

public sealed class OrderingApiTests(OrderingApiTests.Factory factory) : IClassFixture<OrderingApiTests.Factory>
{
    private static readonly ShippingAddress Address = new("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static CreateOrderRequest Request() => new(
    [
        new OrderItem(Guid.NewGuid(), "Keyboard", 100m, 2),
        new OrderItem(Guid.NewGuid(), "Mouse", 25m, 1)
    ], Address);

    [Fact]
    public async Task Orders_are_created_for_the_signed_in_user_and_listed_for_them()
    {
        var user = $"user-{Guid.NewGuid():N}";
        var client = factory.CreateClient().As(user);

        var response = await client.PostAsJsonAsync("/api/orders", Request(), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<Order>(Ct);
        Assert.Equal(user, order?.UserName);
        Assert.Equal(225m, order?.Total);
        Assert.Equal(OrderStatus.Submitted, order?.Status);

        var orders = await client.GetFromJsonAsync<List<Order>>("/api/orders", Ct);
        Assert.Equal(order!.Id, Assert.Single(orders!).Id);
    }

    [Fact]
    public async Task Other_users_cannot_see_an_order_but_admins_can()
    {
        var owner = factory.CreateClient().As($"user-{Guid.NewGuid():N}");
        var order = await (await owner.PostAsJsonAsync("/api/orders", Request(), Ct)).Content.ReadFromJsonAsync<Order>(Ct);

        var stranger = await factory.CreateClient().As("someone-else").GetAsync($"/api/orders/{order!.Id}", Ct);
        var admin = await factory.CreateClient().As("ada", "admin").GetAsync($"/api/orders/{order.Id}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    [Fact]
    public async Task Orders_require_a_signed_in_user()
    {
        var response = await factory.CreateClient().GetAsync("/api/orders", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Create_order_without_items_returns_400()
    {
        var response = await factory.CreateClient().As("ada").PostAsJsonAsync("/api/orders", new CreateOrderRequest([], Address), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<OrderingApiMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services => services.AddTestAuthentication());
    }
}
