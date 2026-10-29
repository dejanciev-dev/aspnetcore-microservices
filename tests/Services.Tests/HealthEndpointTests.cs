using System.Net;
using Catalog.API;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Services.Tests;

/// <summary>Readiness (/health) reflects dependencies; liveness (/alive) only the process.</summary>
public sealed class HealthEndpointTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_failing_dependency_fails_readiness_but_not_liveness()
    {
        // Stands in for an unreachable database or broker.
        await using var catalog = new WebApplicationFactory<CatalogApiMarker>().WithWebHostBuilder(b => b
            .ConfigureTestServices(services => services.AddHealthChecks()
                .AddCheck("database", () => HealthCheckResult.Unhealthy("connection refused"))));
        var client = catalog.CreateClient();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/alive", Ct)).StatusCode);
    }
}
