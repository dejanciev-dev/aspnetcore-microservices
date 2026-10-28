using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Ordering.API;
using Ordering.API.Models;

namespace Services.Tests;

public sealed class OrderingApiTests(WebApplicationFactory<OrderingApiMarker> factory)
    : IClassFixture<WebApplicationFactory<OrderingApiMarker>>
{
    private static readonly ShippingAddress Address = new("Ada Lovelace", "1 Main St", "London", "UK", "N1 9GU");

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Create_order_computes_total_and_is_listed_for_user()
    {
        var request = new CreateOrderRequest("ada",
        [
            new OrderItem(Guid.NewGuid(), "Keyboard", 100m, 2),
            new OrderItem(Guid.NewGuid(), "Mouse", 25m, 1)
        ], Address);

        var response = await _client.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<Order>();
        Assert.Equal(225m, order?.Total);
        Assert.Equal(OrderStatus.Submitted, order?.Status);

        var orders = await _client.GetFromJsonAsync<List<Order>>("/api/orders?userName=ada");
        Assert.Contains(orders!, o => o.Id == order!.Id);
    }

    [Fact]
    public async Task Create_order_without_items_returns_400()
    {
        var response = await _client.PostAsJsonAsync("/api/orders", new CreateOrderRequest("ada", [], Address));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
