using System.Net;
using System.Text;
using ApiGateway;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace Services.Tests;

public sealed class GatewayOutputCacheTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Stands in for every downstream service: counts the calls that get past the gateway and
    /// answers with the call number, so a cached body is recognisable.
    /// </summary>
    private sealed class FakeDownstream : HttpMessageHandler, IForwarderHttpClientFactory
    {
        private int _calls;
        public int Calls => _calls;
        public HttpStatusCode WriteStatus { get; set; } = HttpStatusCode.NoContent;

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            var status = request.Method == HttpMethod.Get
                ? request.RequestUri!.AbsolutePath.EndsWith("/missing") ? HttpStatusCode.NotFound : HttpStatusCode.OK
                : WriteStatus;

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent($$"""{"call":{{call}}}""", Encoding.UTF8, "application/json"),
            });
        }
    }

    private static (WebApplicationFactory<ApiGatewayMarker> Gateway, FakeDownstream Downstream) Gateway()
    {
        var downstream = new FakeDownstream();
        var gateway = new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b => b
            .ConfigureTestServices(services => services.AddSingleton<IForwarderHttpClientFactory>(downstream)));
        return (gateway, downstream);
    }

    [Fact]
    public async Task Repeated_catalog_reads_are_served_from_the_gateway()
    {
        var (gateway, downstream) = Gateway();
        await using var _ = gateway;
        var client = gateway.CreateClient();

        var first = await client.GetAsync("/api/products?page=1", Ct);
        var second = await client.GetAsync("/api/products?page=1", Ct);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(Ct), await second.Content.ReadAsStringAsync(Ct));
        Assert.NotNull(second.Headers.Age);
        Assert.Equal(1, downstream.Calls);
    }

    [Fact]
    public async Task Each_query_string_is_cached_separately()
    {
        var (gateway, downstream) = Gateway();
        await using var _ = gateway;
        var client = gateway.CreateClient();

        var page1 = await client.GetStringAsync("/api/products?page=1", Ct);
        var page2 = await client.GetStringAsync("/api/products?page=2", Ct);

        Assert.NotEqual(page1, page2);
        Assert.Equal(2, downstream.Calls);
    }

    [Fact]
    public async Task Not_found_responses_are_not_cached()
    {
        var (gateway, downstream) = Gateway();
        await using var _ = gateway;
        var client = gateway.CreateClient();

        await client.GetAsync("/api/products/missing", Ct);
        var again = await client.GetAsync("/api/products/missing", Ct);

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(2, downstream.Calls);
    }

    [Fact]
    public async Task Routes_without_a_cache_policy_always_reach_the_service()
    {
        var (gateway, downstream) = Gateway();
        await using var _ = gateway;
        var client = gateway.CreateClient();

        await client.GetAsync("/api/orders", Ct);
        await client.GetAsync("/api/orders", Ct);

        Assert.Equal(2, downstream.Calls);
    }

    [Fact]
    public async Task A_successful_catalog_write_evicts_the_cached_reads()
    {
        var (gateway, downstream) = Gateway();
        await using var _ = gateway;
        var client = gateway.CreateClient();

        var before = await client.GetStringAsync("/api/products", Ct);
        var write = await client.PutAsync($"/api/products/{Guid.NewGuid()}", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);
        var after = await client.GetStringAsync("/api/products", Ct);

        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);
        Assert.NotEqual(before, after);
        Assert.Equal(3, downstream.Calls);
    }

    [Fact]
    public async Task A_rejected_catalog_write_keeps_the_cache()
    {
        var (gateway, downstream) = Gateway();
        await using var _ = gateway;
        downstream.WriteStatus = HttpStatusCode.Forbidden;
        var client = gateway.CreateClient();

        var before = await client.GetStringAsync("/api/products", Ct);
        await client.DeleteAsync($"/api/products/{Guid.NewGuid()}", Ct);
        var after = await client.GetStringAsync("/api/products", Ct);

        Assert.Equal(before, after);
        Assert.Equal(2, downstream.Calls);
    }
}
