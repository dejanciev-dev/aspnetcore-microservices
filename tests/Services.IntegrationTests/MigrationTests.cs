using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Common;
using Catalog.API;
using Catalog.API.Data;
using Catalog.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Services.IntegrationTests;

/// <summary>
/// With several replicas, migrations run once in a separate job; the replicas skip them and stay
/// out of rotation (readiness) until the schema is there.
/// </summary>
public sealed class MigrationTests(Infrastructure infra)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_startup_migration_a_replica_is_alive_but_not_ready_until_the_schema_exists()
    {
        await using var catalog = new WebApplicationFactory<CatalogApiMarker>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:catalogdb", infra.PostgresConnectionString($"catalog_{Guid.NewGuid():N}"))
            .UseSetting("Database:MigrateOnStartup", "false"));
        var client = catalog.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health", Ct)).StatusCode);

        // What the migration job (`Catalog.API migrate`) does: migrate and seed.
        await using (var scope = catalog.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health", Ct)).StatusCode);
        var products = await client.GetFromJsonAsync<PagedResult<Product>>("/api/products", Ct);
        Assert.Equal(8, products!.TotalCount);   // seeded by the migration, not by the replica
    }
}
