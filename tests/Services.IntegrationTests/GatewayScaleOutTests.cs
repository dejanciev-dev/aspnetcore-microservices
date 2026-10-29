using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiGateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Yarp.ReverseProxy.Forwarder;

namespace Services.IntegrationTests;

/// <summary>
/// Two gateway instances sharing Redis behave like one: a sign-in can start on one and finish on
/// the other, and a catalog write through either evicts the cached reads of both.
/// </summary>
public sealed class GatewayScaleOutTests(Infrastructure infra)
{
    private const string PublicHost = "localhost:5100";   // a redirect URI the realm allows
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<ApiGatewayMarker> Replica(Action<IServiceCollection>? services = null) =>
        new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:redis", infra.RedisConnectionString)
             .UseSetting("Identity:Authority", infra.KeycloakRealmUrl)
             .UseSetting("Identity:ClientSecret", "storefront-bff-dev-secret");
            if (services is not null) b.ConfigureTestServices(services);
        });

    [Fact]
    public async Task A_sign_in_started_on_one_replica_completes_on_another()
    {
        await using var replicaA = Replica();
        await using var replicaB = Replica();
        var a = replicaA.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var b = replicaB.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

        // Replica A starts the flow and sets the encrypted correlation and nonce cookies.
        var challenge = await a.SendAsync(Get("/bff/login?returnUrl=/orders"), Ct);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var oidcCookies = Cookies(challenge);

        // Ada signs in at Keycloak, which redirects the browser to /signin-oidc.
        var callback = await KeycloakLogin.SignInAsync(infra.KeycloakRealmUrl, challenge.Headers.Location!, "ada", "ada", Ct);

        // The load balancer sends the callback to replica B: it must decrypt A's cookies.
        var signedIn = await b.SendAsync(Get(callback.PathAndQuery, oidcCookies), Ct);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/orders", signedIn.Headers.Location!.ToString());
        var session = Cookies(signedIn);

        // The session cookie B issued is valid on A as well.
        foreach (var replica in new[] { a, b })
        {
            var user = await (await replica.SendAsync(Get("/bff/user", session), Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.True(user.GetProperty("isAuthenticated").GetBoolean());
            Assert.Equal("ada", user.GetProperty("name").GetString());
        }

        // And the keys really come from Redis, not from a key folder the two test hosts happen to share.
        await using var redis = await ConnectionMultiplexer.ConnectAsync(infra.RedisConnectionString);
        Assert.True(await redis.GetDatabase().ListLengthAsync(SharedState.DataProtectionKeys) > 0);
    }

    [Fact]
    public async Task A_write_through_one_replica_evicts_cached_reads_on_all_of_them()
    {
        var catalog = new CountingCatalog();
        await using var replicaA = Replica(s => s.AddSingleton<IForwarderHttpClientFactory>(catalog));
        await using var replicaB = Replica(s => s.AddSingleton<IForwarderHttpClientFactory>(catalog));
        var a = replicaA.CreateClient();
        var b = replicaB.CreateClient();
        var url = $"/api/products?search={Guid.NewGuid():N}";   // a cache key no other run has used

        var first = await a.GetStringAsync(url, Ct);
        var fromSharedCache = await b.GetStringAsync(url, Ct);
        Assert.Equal(first, fromSharedCache);
        Assert.Equal(1, catalog.Calls);

        var write = await b.PutAsync($"/api/products/{Guid.NewGuid()}", new StringContent("{}", Encoding.UTF8, "application/json"), Ct);
        Assert.Equal(HttpStatusCode.NoContent, write.StatusCode);

        var afterWrite = await a.GetStringAsync(url, Ct);
        Assert.NotEqual(first, afterWrite);
        Assert.Equal(3, catalog.Calls);
    }

    private static HttpRequestMessage Get(string path, string? cookies = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-Host", PublicHost);
        if (!string.IsNullOrEmpty(cookies)) request.Headers.Add("Cookie", cookies);
        return request;
    }

    /// <summary>The name=value pairs a response sets, as a Cookie header for the next request.</summary>
    private static string Cookies(HttpResponseMessage response) =>
        string.Join("; ", (response.Headers.TryGetValues("Set-Cookie", out var values) ? values : [])
            .Select(c => c.Split(';', 2)[0])
            .Where(c => !c.EndsWith('=')));

    /// <summary>Stands in for Catalog.API behind both replicas; answers with a call counter.</summary>
    private sealed class CountingCatalog : HttpMessageHandler, IForwarderHttpClientFactory
    {
        private int _calls;
        public int Calls => _calls;

        public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(this, disposeHandler: false);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            return Task.FromResult(request.Method == HttpMethod.Get
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"call":{{call}}}""", Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
