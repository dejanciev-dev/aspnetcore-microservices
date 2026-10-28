using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Common;
using Catalog.API;
using Catalog.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Services.Tests;

public sealed class CatalogApiTests(CatalogApiTests.Factory factory) : IClassFixture<CatalogApiTests.Factory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Browsing_is_public()
    {
        var page = await factory.CreateClient().GetFromJsonAsync<PagedResult<Product>>("/api/products?pageSize=100", Ct);

        Assert.NotNull(page);
        Assert.True(page.TotalCount >= 8);
    }

    [Fact]
    public async Task List_filters_by_category()
    {
        var page = await factory.CreateClient().GetFromJsonAsync<PagedResult<Product>>("/api/products?category=Audio", Ct);

        Assert.NotNull(page);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, p => Assert.Equal("Audio", p.Category));
    }

    [Fact]
    public async Task Get_unknown_product_returns_404()
    {
        var response = await factory.CreateClient().GetAsync($"/api/products/{Guid.NewGuid()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_create_then_get()
    {
        var admin = factory.CreateClient().As("ada", "admin");
        var request = new UpsertProductRequest("Webcam", "1080p webcam", "Peripherals", 79.99m, null);

        var created = await admin.PostAsJsonAsync("/api/products", request, Ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var product = await admin.GetFromJsonAsync<Product>(created.Headers.Location, Ct);
        Assert.Equal("Webcam", product?.Name);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("customer", HttpStatusCode.Forbidden)]
    public async Task Managing_products_requires_the_admin_role(string? role, HttpStatusCode expected)
    {
        var client = factory.CreateClient();
        if (role is not null) client.As("grace", role);

        var response = await client.PostAsJsonAsync("/api/products", new UpsertProductRequest("Webcam", "", "Peripherals", 79.99m, null), Ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Create_with_invalid_price_returns_400()
    {
        var request = new UpsertProductRequest("Freebie", "", "Peripherals", 0m, null);

        var response = await factory.CreateClient().As("ada", "admin").PostAsJsonAsync("/api/products", request, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<CatalogApiMarker>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureTestServices(services => services.AddTestAuthentication());
    }
}
