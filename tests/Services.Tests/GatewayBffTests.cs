using System.Net;
using System.Net.Http.Json;
using ApiGateway;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Services.Tests;

public sealed class GatewayBffTests(WebApplicationFactory<ApiGatewayMarker> factory) : IClassFixture<WebApplicationFactory<ApiGatewayMarker>>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record BffUser(bool IsAuthenticated, string? Name, string[] Roles);

    [Fact]
    public async Task User_endpoint_reports_anonymous_visitors()
    {
        var user = await factory.CreateClient().GetFromJsonAsync<BffUser>("/bff/user", Ct);

        Assert.False(user!.IsAuthenticated);
        Assert.Empty(user.Roles);
    }

    [Fact]
    public async Task Cookie_authenticated_api_calls_without_the_csrf_header_are_rejected()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/basket/items");
        request.Headers.Add("Cookie", "storefront.session=anything");

        var response = await factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Requests_with_the_csrf_header_reach_the_proxy()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/products");
        request.Headers.Add("Cookie", "storefront.session=anything");
        request.Headers.Add("X-CSRF", "1");

        var response = await factory.CreateClient().SendAsync(request, Ct);

        // No Catalog is running in this test, so YARP answers 502: the request got past the CSRF check.
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }
}
