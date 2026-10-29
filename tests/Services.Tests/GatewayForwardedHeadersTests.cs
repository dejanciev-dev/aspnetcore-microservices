using System.Net;
using ApiGateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Services.Tests;

/// <summary>
/// Behind a cloud ingress, nginx and another internal ingress, X-Forwarded-For holds several hops.
/// Rate-limit partitions must still be per real client.
/// </summary>
public sealed class GatewayForwardedHeadersTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<ApiGatewayMarker> Gateway(string? knownNetwork) =>
        new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("RateLimiting:Anonymous:TokenLimit", "1")
             .UseSetting("RateLimiting:Anonymous:TokensPerPeriod", "1")
             .UseSetting("RateLimiting:Anonymous:ReplenishmentPeriodSeconds", "3600");
            if (knownNetwork is not null) b.UseSetting("ForwardedHeaders:KnownNetworks:0", knownNetwork);

            // Every request reaches the gateway from the last internal proxy.
            b.ConfigureTestServices(services => services.AddSingleton<IStartupFilter>(new FromProxy(IPAddress.Parse("10.0.1.9"))));
        });

    private static HttpRequestMessage ThroughProxies(string client)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/bff/user");
        request.Headers.Add("X-Forwarded-For", $"{client}, 10.0.1.7");   // client, then nginx's view of the ingress
        return request;
    }

    [Fact]
    public async Task With_the_proxy_network_known_each_client_keeps_its_own_budget()
    {
        await using var gateway = Gateway("10.0.0.0/16");
        var client = gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ThroughProxies("203.0.113.50"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ThroughProxies("203.0.113.51"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(ThroughProxies("203.0.113.50"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Without_it_only_one_hop_is_unwound_and_clients_behind_the_same_proxy_share_a_budget()
    {
        await using var gateway = Gateway(knownNetwork: null);
        var client = gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ThroughProxies("203.0.113.60"), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(ThroughProxies("203.0.113.61"), Ct)).StatusCode);
    }

    private sealed class FromProxy(IPAddress proxy) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = proxy;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
