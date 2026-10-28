using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ApiGateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Services.Tests;

public sealed class GatewayRateLimitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Tiny limits that never refill during a test run.
    private static WebApplicationFactory<ApiGatewayMarker> Gateway() =>
        new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b => b
            .UseSetting("RateLimiting:Anonymous:TokenLimit", "3")
            .UseSetting("RateLimiting:Anonymous:TokensPerPeriod", "1")
            .UseSetting("RateLimiting:Anonymous:ReplenishmentPeriodSeconds", "3600")
            .UseSetting("RateLimiting:Checkout:PermitLimit", "2")
            .UseSetting("RateLimiting:Checkout:WindowSeconds", "3600"));

    private static HttpRequestMessage From(string ip, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Forwarded-For", ip);
        return request;
    }

    [Fact]
    public async Task Callers_over_the_global_limit_get_429_with_retry_after_and_problem_details()
    {
        await using var gateway = Gateway();
        var client = gateway.CreateClient();

        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(From("203.0.113.10", HttpMethod.Get, "/bff/user"), Ct)).StatusCode);

        var rejected = await client.SendAsync(From("203.0.113.10", HttpMethod.Get, "/bff/user"), Ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal("application/problem+json", rejected.Content.Headers.ContentType?.MediaType);
        var problem = await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("Too many requests", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Each_client_ip_has_its_own_budget()
    {
        await using var gateway = Gateway();
        var client = gateway.CreateClient();

        for (var i = 0; i < 4; i++) await client.SendAsync(From("203.0.113.20", HttpMethod.Get, "/bff/user"), Ct);
        var other = await client.SendAsync(From("203.0.113.21", HttpMethod.Get, "/bff/user"), Ct);

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Health_checks_are_never_limited()
    {
        await using var gateway = Gateway();
        var client = gateway.CreateClient();

        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(From("203.0.113.30", HttpMethod.Get, "/health"), Ct)).StatusCode);
    }

    [Fact]
    public async Task Checkout_has_a_stricter_route_policy_than_the_rest_of_the_basket()
    {
        await using var gateway = new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b => b
            .UseSetting("RateLimiting:Checkout:PermitLimit", "2")
            .UseSetting("RateLimiting:Checkout:WindowSeconds", "3600"));
        var client = gateway.CreateClient();

        // No Basket.API runs here: requests that pass the limiter end in 502 from YARP.
        var checkouts = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
            checkouts.Add((await client.SendAsync(From("203.0.113.40", HttpMethod.Post, "/api/basket/checkout"), Ct)).StatusCode);
        var otherBasketCall = await client.SendAsync(From("203.0.113.40", HttpMethod.Post, "/api/basket/items"), Ct);

        Assert.Equal([HttpStatusCode.BadGateway, HttpStatusCode.BadGateway, HttpStatusCode.TooManyRequests], checkouts);
        Assert.Equal(HttpStatusCode.BadGateway, otherBasketCall.StatusCode);
    }

    [Fact]
    public void Signed_in_users_are_partitioned_by_name_and_anonymous_callers_by_ip()
    {
        var anonymous = new DefaultHttpContext();
        anonymous.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");

        var signedIn = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("preferred_username", "ada")], "cookie", "preferred_username", "role")),
        };
        signedIn.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.7");

        Assert.Equal("ip:198.51.100.7", RateLimiting.PartitionKey(anonymous));
        Assert.Equal("user:ada", RateLimiting.PartitionKey(signedIn));
    }
}
