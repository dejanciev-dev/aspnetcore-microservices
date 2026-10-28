using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Common;
using Catalog.API;
using Catalog.API.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Services.Tests;

public sealed class CatalogApiTests(WebApplicationFactory<CatalogApiMarker> factory)
    : IClassFixture<WebApplicationFactory<CatalogApiMarker>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task List_returns_seeded_products()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<Product>>("/api/products?pageSize=100", TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.True(page.TotalCount >= 8);
    }

    [Fact]
    public async Task List_filters_by_category()
    {
        var page = await _client.GetFromJsonAsync<PagedResult<Product>>("/api/products?category=Audio", TestContext.Current.CancellationToken);

        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, p => Assert.Equal("Audio", p.Category));
    }

    [Fact]
    public async Task Get_unknown_product_returns_404()
    {
        var response = await _client.GetAsync($"/api/products/{Guid.NewGuid()}", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var request = new UpsertProductRequest("Webcam", "1080p webcam", "Peripherals", 79.99m, null);

        var created = await _client.PostAsJsonAsync("/api/products", request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var product = await _client.GetFromJsonAsync<Product>(created.Headers.Location, TestContext.Current.CancellationToken);
        Assert.Equal("Webcam", product?.Name);
    }

    [Fact]
    public async Task Create_with_invalid_price_returns_400()
    {
        var request = new UpsertProductRequest("Freebie", "", "Peripherals", 0m, null);

        var response = await _client.PostAsJsonAsync("/api/products", request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
