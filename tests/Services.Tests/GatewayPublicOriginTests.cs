using System.Net;
using ApiGateway;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Services.Tests;

/// <summary>Which redirect URI the gateway sends to Keycloak when it sits behind proxies.</summary>
public sealed class GatewayPublicOriginTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WebApplicationFactory<ApiGatewayMarker> Gateway(string? publicOrigin = null) =>
        new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b =>
        {
            if (publicOrigin is not null) b.UseSetting("Bff:PublicOrigin", publicOrigin);
            // A static provider configuration instead of discovery, so no Keycloak is needed.
            b.ConfigureTestServices(services => services.Configure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme,
                o => o.Configuration = new OpenIdConnectConfiguration { AuthorizationEndpoint = "https://idp.example/authorize" }));
        });

    private static async Task<string> RedirectUriSentToKeycloakAsync(WebApplicationFactory<ApiGatewayMarker> gateway)
    {
        var client = gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var request = new HttpRequestMessage(HttpMethod.Get, "/bff/login");
        request.Headers.Add("X-Forwarded-Host", "localhost:4200");   // what nginx or the dev proxy sends
        request.Headers.Add("X-Forwarded-Proto", "http");

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("https://idp.example/authorize", response.Headers.Location!.ToString());
        return QueryHelpers.ParseQuery(response.Headers.Location.Query)["redirect_uri"].ToString();
    }

    [Fact]
    public async Task Without_a_public_origin_the_forwarded_headers_decide()
    {
        await using var gateway = Gateway();

        Assert.Equal("http://localhost:4200/signin-oidc", await RedirectUriSentToKeycloakAsync(gateway));
    }

    [Fact]
    public async Task A_configured_public_origin_wins_over_the_forwarded_headers()
    {
        await using var gateway = Gateway("https://shop.example.com");

        Assert.Equal("https://shop.example.com/signin-oidc", await RedirectUriSentToKeycloakAsync(gateway));
    }
}
