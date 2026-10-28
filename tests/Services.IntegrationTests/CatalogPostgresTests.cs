using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Common;
using Catalog.API;
using Catalog.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Services.IntegrationTests;

public sealed class CatalogPostgresTests(Infrastructure infra)
{
    // A fresh database per test keeps the seeded data predictable.
    private WebApplicationFactory<CatalogApiMarker> CreateApp(string? database = null)
    {
        var connectionString = infra.PostgresConnectionString(database ?? $"catalog_{Guid.NewGuid():N}");
        return new WebApplicationFactory<CatalogApiMarker>().WithWebHostBuilder(b =>
            b.UseSetting("ConnectionStrings:catalogdb", connectionString));
    }

    [Fact]
    public async Task Migrations_create_the_schema_and_seed_products()
    {
        await using var app = CreateApp();
        var client = app.CreateClient();

        var page = await client.GetFromJsonAsync<PagedResult<Product>>("/api/products?pageSize=100", TestContext.Current.CancellationToken);
        var categories = await client.GetFromJsonAsync<List<string>>("/api/products/categories", TestContext.Current.CancellationToken);

        Assert.Equal(8, page!.TotalCount);
        Assert.Equal(["Accessories", "Audio", "Displays", "Peripherals"], categories);
    }

    [Fact]
    public async Task Search_is_case_insensitive_and_treats_wildcards_literally()
    {
        await using var app = CreateApp();
        var client = app.CreateClient();

        var monitors = await client.GetFromJsonAsync<PagedResult<Product>>("/api/products?search=MONITOR", TestContext.Current.CancellationToken);
        var wildcard = await client.GetFromJsonAsync<PagedResult<Product>>("/api/products?search=%25", TestContext.Current.CancellationToken);

        Assert.Equal(2, monitors!.TotalCount);
        Assert.Equal(0, wildcard!.TotalCount);
    }

    [Fact]
    public async Task Filtering_and_paging_are_done_in_the_database()
    {
        await using var app = CreateApp();
        var client = app.CreateClient();

        var page = await client.GetFromJsonAsync<PagedResult<Product>>("/api/products?category=displays&page=2&pageSize=1", TestContext.Current.CancellationToken);

        Assert.Equal(2, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal("Ultrawide Monitor", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Create_update_and_delete_are_persisted_across_restarts()
    {
        var database = $"catalog_{Guid.NewGuid():N}";
        var ct = TestContext.Current.CancellationToken;
        Uri location;

        await using (var app = CreateApp(database))
        {
            var client = app.CreateClient();
            var created = await client.PostAsJsonAsync("/api/products", new UpsertProductRequest("Webcam", "1080p", "Peripherals", 79.99m, null), ct);
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            location = created.Headers.Location!;

            var updated = await client.PutAsJsonAsync(location, new UpsertProductRequest("Webcam Pro", "4K", "Peripherals", 129m, null), ct);
            Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        }

        // A brand-new app instance only sees what was actually written to PostgreSQL.
        await using (var app = CreateApp(database))
        {
            var client = app.CreateClient();
            var product = await client.GetFromJsonAsync<Product>(location, ct);
            Assert.Equal("Webcam Pro", product!.Name);
            Assert.Equal(129m, product.Price);

            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(location, ct)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(location, ct)).StatusCode);
        }
    }
}
