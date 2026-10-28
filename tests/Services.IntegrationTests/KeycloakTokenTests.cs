using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Basket.API;
using Basket.API.Clients;
using Basket.API.Models;
using Catalog.API;
using Catalog.API.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Services.IntegrationTests;

/// <summary>
/// The services' real JWT validation against a real Keycloak running the project's realm:
/// issuer and signing keys from discovery, the storefront-api audience and realm-role mapping.
/// </summary>
public sealed class KeycloakTokenTests(Infrastructure infra)
{
    private static readonly Guid KeyboardId = Guid.Parse("4f1c9a0e-0001-4a6e-9b1a-000000000001");
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Admin_role_from_keycloak_allows_managing_products_and_customers_are_forbidden()
    {
        await using var catalog = CatalogApp();
        var update = new UpsertProductRequest("Mechanical Keyboard", "Hot-swappable 75% keyboard.", "Peripherals", 119.00m, null);

        var asGrace = await SendAsync(catalog.CreateClient(), HttpMethod.Put, $"/api/products/{KeyboardId}", update, await TokenAsync("grace", "grace"));
        var asAda = await SendAsync(catalog.CreateClient(), HttpMethod.Put, $"/api/products/{KeyboardId}", update, await TokenAsync("ada", "ada"));
        var anonymous = await SendAsync(catalog.CreateClient(), HttpMethod.Put, $"/api/products/{KeyboardId}", update, token: null);

        Assert.Equal(HttpStatusCode.Forbidden, asGrace.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, asAda.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task The_basket_belongs_to_the_user_in_the_token()
    {
        await using var basket = BasketApp();
        var token = await TokenAsync("grace", "grace");

        var response = await SendAsync(basket.CreateClient(), HttpMethod.Get, "/api/basket", body: null, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("grace", (await response.Content.ReadFromJsonAsync<ShoppingCart>(Ct))!.UserName);
    }

    [Fact]
    public async Task A_tampered_token_is_rejected()
    {
        await using var basket = BasketApp();
        var token = await TokenAsync("grace", "grace");
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");   // break the signature

        var response = await SendAsync(basket.CreateClient(), HttpMethod.Get, "/api/basket", body: null, tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---------- helpers ----------

    // No test auth handler here: these apps validate real Keycloak tokens.
    private WebApplicationFactory<CatalogApiMarker> CatalogApp() =>
        new WebApplicationFactory<CatalogApiMarker>().WithWebHostBuilder(b => b
            .UseSetting("Identity:Authority", infra.KeycloakRealmUrl));

    private WebApplicationFactory<BasketApiMarker> BasketApp() =>
        new WebApplicationFactory<BasketApiMarker>().WithWebHostBuilder(b => b
            .UseSetting("Identity:Authority", infra.KeycloakRealmUrl)
            .ConfigureTestServices(s => s.AddSingleton<ICatalogClient>(new NoCatalog())));

    /// <summary>Password grant on the test-only client of the realm.</summary>
    private async Task<string> TokenAsync(string user, string password)
    {
        using var http = new HttpClient();
        using var response = await http.PostAsync($"{infra.KeycloakRealmUrl}/protocol/openid-connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = "storefront-tests",
            ["username"] = user,
            ["password"] = password,
            ["scope"] = "openid",
        }), Ct);

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenResponse>(Ct))!.AccessToken;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, object? body, string? token)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, Ct);
    }

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed class NoCatalog : ICatalogClient
    {
        public Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct = default) => Task.FromResult<CatalogProduct?>(null);
    }
}
