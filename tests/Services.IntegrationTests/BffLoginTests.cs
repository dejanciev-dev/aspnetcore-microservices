using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using ApiGateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Services.IntegrationTests;

/// <summary>
/// The full BFF sign-in against a real Keycloak, driven the way a browser would:
/// gateway /bff/login → Keycloak login form → /signin-oidc callback → session cookie → token relay.
/// </summary>
public sealed partial class BffLoginTests(Infrastructure infra)
{
    // The realm allows redirects to http://localhost:5100/signin-oidc; the gateway honours X-Forwarded-Host.
    private const string PublicHost = "localhost:5100";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Signing_in_creates_a_session_and_api_calls_carry_the_users_access_token()
    {
        await using var echo = await EchoAuthorizationServer.StartAsync(Ct);
        await using var gateway = new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b => b
            .UseSetting("Identity:Authority", infra.KeycloakRealmUrl)
            .UseSetting("Identity:ClientSecret", "storefront-bff-dev-secret")
            .UseSetting("ReverseProxy:Clusters:basket:Destinations:d1:Address", echo.Url));
        var browser = gateway.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        browser.DefaultRequestHeaders.Add("X-Forwarded-Host", PublicHost);

        // 1. The gateway starts the OIDC flow (PAR + PKCE) and redirects to Keycloak.
        var challenge = await browser.GetAsync("/bff/login?returnUrl=/orders", Ct);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.StartsWith(infra.KeycloakRealmUrl, challenge.Headers.Location!.ToString());

        // 2. Keycloak shows its login page; submit ada's credentials.
        using var keycloak = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() });
        var loginPage = await OpenKeycloakPageAsync(keycloak, challenge.Headers.Location!);
        var formAction = WebUtility.HtmlDecode(LoginFormAction().Match(loginPage).Groups["action"].Value);
        var login = await keycloak.PostAsync(formAction, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "ada",
            ["password"] = "ada",
        }), Ct);

        Assert.Equal(HttpStatusCode.Found, login.StatusCode);
        var callback = login.Headers.Location!;
        Assert.Equal($"http://{PublicHost}/signin-oidc", callback.GetLeftPart(UriPartial.Path));

        // 3. The browser follows Keycloak's redirect back to the gateway, which redeems the code.
        var signedIn = await browser.GetAsync(callback.PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal("/orders", signedIn.Headers.Location!.ToString());
        Assert.Contains(signedIn.Headers.GetValues("Set-Cookie"), c => c.StartsWith("storefront.session") && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));

        // 4. The session is visible to the SPA, including Keycloak realm roles.
        var user = await browser.GetFromJsonAsync<JsonElement>("/bff/user", Ct);
        Assert.True(user.GetProperty("isAuthenticated").GetBoolean());
        Assert.Equal("ada", user.GetProperty("name").GetString());
        Assert.Contains("admin", user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));

        // 5. Proxied API calls carry ada's access token (the browser never saw it).
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/basket");
        request.Headers.Add("X-CSRF", "1");
        var relayed = await browser.SendAsync(request, Ct);
        var authorization = await relayed.Content.ReadAsStringAsync(Ct);

        Assert.StartsWith("Bearer ", authorization);
        var token = new JsonWebToken(authorization["Bearer ".Length..]);
        Assert.Equal("ada", token.GetClaim("preferred_username").Value);
        Assert.Contains("storefront-api", token.Audiences);

        // 6. Signing out ends the Keycloak session too.
        var logout = await browser.GetAsync("/bff/logout", Ct);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Contains("/protocol/openid-connect/logout", logout.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Anonymous_api_calls_are_proxied_without_credentials()
    {
        await using var echo = await EchoAuthorizationServer.StartAsync(Ct);
        await using var gateway = new WebApplicationFactory<ApiGatewayMarker>().WithWebHostBuilder(b => b
            .UseSetting("Identity:Authority", infra.KeycloakRealmUrl)
            .UseSetting("ReverseProxy:Clusters:catalog:Destinations:d1:Address", echo.Url));

        var response = await gateway.CreateClient().GetStringAsync("/api/products", Ct);

        Assert.Equal("", response);
    }

    /// <summary>
    /// Follows Keycloak's own redirects (it may hop through login-actions URLs) like a browser would,
    /// and fails with the target if Keycloak sends the browser anywhere else, e.g. back with an error.
    /// </summary>
    private async Task<string> OpenKeycloakPageAsync(HttpClient keycloak, Uri url)
    {
        for (var hop = 0; hop < 5; hop++)
        {
            using var response = await keycloak.GetAsync(url, Ct);
            if (response.IsSuccessStatusCode) return await response.Content.ReadAsStringAsync(Ct);

            var location = response.Headers.Location;
            if (location is null || !new Uri(url, location).ToString().StartsWith(infra.KeycloakRealmUrl))
                Assert.Fail($"Keycloak answered {(int)response.StatusCode} for {url}; Location: {location}; body: {await response.Content.ReadAsStringAsync(Ct)}");

            url = new Uri(url, location);
        }

        Assert.Fail("Too many redirects inside Keycloak.");
        return "";
    }

    [GeneratedRegex("<form[^>]*action=\"(?<action>[^\"]+)\"")]
    private static partial Regex LoginFormAction();

    /// <summary>A tiny real HTTP server standing in for a downstream API: it returns the Authorization header it received.</summary>
    private sealed class EchoAuthorizationServer(WebApplication app, string url) : IAsyncDisposable
    {
        public string Url { get; } = url;

        public static async Task<EchoAuthorizationServer> StartAsync(CancellationToken ct)
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.Run(context => context.Response.WriteAsync(context.Request.Headers.Authorization.ToString(), ct));
            await app.StartAsync(ct);

            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
            return new EchoAuthorizationServer(app, address);
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }
}
