using Azure.Monitor.OpenTelemetry.AspNetCore;
using Catalog.API;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Instrumentation.AspNetCore;

namespace Services.Tests;

/// <summary>Where telemetry goes, and that probes stay out of traces whichever exporter is on.</summary>
public sealed class TelemetryTests
{
    // Well-formed but unreachable: nothing is sent during the test.
    private const string ConnectionString =
        "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=http://127.0.0.1:9/";

    private static WebApplicationFactory<CatalogApiMarker> Catalog(string? connectionString) =>
        new WebApplicationFactory<CatalogApiMarker>().WithWebHostBuilder(b =>
        {
            if (connectionString is not null) b.UseSetting("APPLICATIONINSIGHTS_CONNECTION_STRING", connectionString);
        });

    private static Func<HttpContext, bool> TraceFilter(IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<AspNetCoreTraceInstrumentationOptions>>().Get(Options.DefaultName).Filter!;

    private static HttpContext Request(string path) => new DefaultHttpContext { Request = { Path = path } };

    // The distro binds its options from configuration only when UseAzureMonitor() was called.
    private static string? AzureMonitorConnectionString(IServiceProvider services) =>
        services.GetRequiredService<IOptionsMonitor<AzureMonitorOptions>>().Get(Options.DefaultName).ConnectionString;

    [Fact]
    public async Task Without_a_connection_string_Azure_Monitor_stays_off()
    {
        await using var catalog = Catalog(connectionString: null);

        Assert.Null(AzureMonitorConnectionString(catalog.Services));
    }

    [Fact]
    public async Task A_connection_string_turns_on_the_Azure_Monitor_exporter()
    {
        await using var catalog = Catalog(ConnectionString);

        Assert.Equal(ConnectionString, AzureMonitorConnectionString(catalog.Services));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ConnectionString)]
    public async Task Health_probes_are_not_traced(string? connectionString)
    {
        await using var catalog = Catalog(connectionString);
        var filter = TraceFilter(catalog.Services);

        Assert.False(filter(Request("/health")));
        Assert.False(filter(Request("/alive")));
        Assert.True(filter(Request("/api/products")));
    }
}
