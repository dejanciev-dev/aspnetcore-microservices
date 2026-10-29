using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway;

/// <summary>
/// Backend-for-frontend: the gateway signs users in with Keycloak (authorization code + PKCE,
/// confidential client) and keeps the tokens server-side in an encrypted, HttpOnly cookie.
/// The Angular app never sees a token; proxied <c>/api</c> calls get the access token attached here.
/// </summary>
internal static class Bff
{
    public const string CookieName = "storefront.session";
    public const string CsrfHeader = "X-CSRF";

    public static WebApplicationBuilder AddBff(this WebApplicationBuilder builder)
    {
        var identity = builder.Configuration.GetSection("Identity");

        // Behind nginx or the Angular dev proxy: honour X-Forwarded-* so redirect URIs use the public host.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        builder.Services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
            })
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;   // Strict would drop the cookie on the redirect back from Keycloak
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;

                // XHR callers get status codes, not redirects to a login page.
                options.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
                options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
                options.Events.OnValidatePrincipal = TokenRefresher.RefreshIfExpiringAsync;
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = identity["Authority"];
                if (identity["MetadataAddress"] is { Length: > 0 } metadata) options.MetadataAddress = metadata;
                options.ClientId = identity["ClientId"];
                options.ClientSecret = identity["ClientSecret"];
                options.RequireHttpsMetadata = identity.GetValue("RequireHttpsMetadata", !builder.Environment.IsDevelopment());

                options.ResponseType = "code";
                options.UsePkce = true;

                // Keycloak returns the code with a top-level GET redirect rather than the default
                // cross-site form POST, so the Lax cookies below are sent on the callback.
                options.ResponseMode = OpenIdConnectResponseMode.Query;

                // The defaults (SameSite=None; Secure) are dropped over plain HTTP, which breaks local
                // sign-in with "Correlation failed". With query response mode, Lax works and stays safe.
                options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SameSite = SameSiteMode.Lax;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SaveTokens = true;   // access, refresh and id token go into the (encrypted) auth cookie
                options.MapInboundClaims = false;
                options.Scope.Clear();
                foreach (var scope in new[] { "openid", "profile", "email" }) options.Scope.Add(scope);

                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = "role";
                options.Events.OnTokenValidated = ctx =>
                {
                    // Realm roles are only in the access token; copy them so /bff/user can report them.
                    if (ctx.TokenEndpointResponse?.AccessToken is { } accessToken && ctx.Principal?.Identity is ClaimsIdentity user)
                        CopyRealmRoles(new JsonWebToken(accessToken), user);
                    return Task.CompletedTask;
                };
            });

        builder.Services.AddAuthorization();
        return builder;
    }

    /// <summary>BFF token relay: replaces the cookie with the user's access token on proxied requests.</summary>
    public static void AddAccessTokenTransform(TransformBuilderContext context) =>
        context.AddRequestTransform(async transform =>
        {
            if (await transform.HttpContext.GetTokenAsync("access_token") is { } token)
                transform.ProxyRequest.Headers.Authorization = new("Bearer", token);
        });

    /// <summary>
    /// With <c>Bff:PublicOrigin</c> set (e.g. https://shop.example.com), every request is treated as
    /// arriving on that origin, so OIDC redirect URIs and Secure cookies don't depend on which
    /// X-Forwarded-* headers a chain of proxies (Container Apps ingress → nginx → here) keeps.
    /// Without it, the forwarded headers decide, as with Docker Compose and the dev proxy.
    /// </summary>
    public static IApplicationBuilder UsePublicOrigin(this IApplicationBuilder app)
    {
        var configured = app.ApplicationServices.GetRequiredService<IConfiguration>()["Bff:PublicOrigin"];
        if (string.IsNullOrWhiteSpace(configured)) return app;

        var origin = new Uri(configured, UriKind.Absolute);
        return app.Use((context, next) =>
        {
            context.Request.Scheme = origin.Scheme;
            context.Request.Host = HostString.FromUriComponent(origin.Authority);   // Authority omits default ports
            return next(context);
        });
    }

    /// <summary>
    /// CSRF protection for cookie-authenticated API calls: a custom header can't be added by a
    /// cross-site form or link, and cross-origin scripts would need a CORS preflight we never allow.
    /// </summary>
    public static IApplicationBuilder UseCsrfHeaderCheck(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api")
                && context.Request.Cookies.ContainsKey(CookieName)
                && !context.Request.Headers.ContainsKey(CsrfHeader))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { title = $"Missing {CsrfHeader} header." });
                return;
            }

            await next(context);
        });

    public static IEndpointRouteBuilder MapBffEndpoints(this IEndpointRouteBuilder app)
    {
        var bff = app.MapGroup("/bff").WithTags("BFF");

        bff.MapGet("/login", (string? returnUrl) =>
                TypedResults.Challenge(new AuthenticationProperties { RedirectUri = LocalPath(returnUrl) }))
            .RequireRateLimiting(RateLimiting.LoginPolicy);   // each start costs Keycloak a PAR request

        // Ends the local session and the Keycloak SSO session, then returns to the storefront.
        bff.MapGet("/logout", () =>
            TypedResults.SignOut(new AuthenticationProperties { RedirectUri = "/" },
                [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));

        bff.MapGet("/user", (ClaimsPrincipal user) => TypedResults.Ok(BffUser.From(user)));

        return app;
    }

    // Only same-site relative paths, so /bff/login can't be abused as an open redirect.
    private static string LocalPath(string? returnUrl) =>
        returnUrl is { Length: > 0 } && returnUrl[0] == '/' && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\")
            ? returnUrl
            : "/";

    private static void CopyRealmRoles(JsonWebToken accessToken, ClaimsIdentity user)
    {
        if (!accessToken.TryGetClaim("realm_access", out var realmAccess)) return;

        var fromToken = new ClaimsIdentity([realmAccess], "token", "preferred_username", user.RoleClaimType);
        Authentication.AddKeycloakRealmRoles(new ClaimsPrincipal(fromToken));
        user.AddClaims(fromToken.FindAll(user.RoleClaimType));
    }
}

internal sealed record BffUser(bool IsAuthenticated, string? Name, string? DisplayName, IReadOnlyList<string> Roles)
{
    public static BffUser From(ClaimsPrincipal user) => user.Identity is { IsAuthenticated: true } identity
        ? new(true, identity.Name, user.FindFirst("name")?.Value ?? identity.Name,
              user.FindAll(((ClaimsIdentity)identity).RoleClaimType).Select(c => c.Value).Distinct().Order().ToList())
        : new(false, null, null, []);
}

/// <summary>
/// Keycloak access tokens are short-lived (5 minutes here). Before one expires, exchange the stored
/// refresh token for a new set and re-issue the cookie; if that fails the session ends.
/// </summary>
internal static class TokenRefresher
{
    private static readonly TimeSpan RefreshAhead = TimeSpan.FromSeconds(60);

    public static async Task RefreshIfExpiringAsync(CookieValidatePrincipalContext context)
    {
        if (context.Properties.GetTokenValue("expires_at") is not { } expiresAtText
            || DateTimeOffset.Parse(expiresAtText, CultureInfo.InvariantCulture) - RefreshAhead > DateTimeOffset.UtcNow)
            return;

        var ct = context.HttpContext.RequestAborted;
        var oidc = context.HttpContext.RequestServices
            .GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
            .Get(OpenIdConnectDefaults.AuthenticationScheme);

        if (context.Properties.GetTokenValue("refresh_token") is not { } refreshToken || oidc.ConfigurationManager is null)
        {
            await RejectAsync(context);
            return;
        }

        var configuration = await oidc.ConfigurationManager.GetConfigurationAsync(ct);
        using var response = await oidc.Backchannel.PostAsync(configuration.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = oidc.ClientId!,
            ["client_secret"] = oidc.ClientSecret!,
        }), ct);

        if (!response.IsSuccessStatusCode || await response.Content.ReadFromJsonAsync<TokenResponse>(ct) is not { } tokens)
        {
            await RejectAsync(context);   // refresh token expired or session revoked in Keycloak
            return;
        }

        context.Properties.UpdateTokenValue("access_token", tokens.AccessToken);
        context.Properties.UpdateTokenValue("refresh_token", tokens.RefreshToken ?? refreshToken);
        if (tokens.IdToken is not null) context.Properties.UpdateTokenValue("id_token", tokens.IdToken);
        context.Properties.UpdateTokenValue("expires_at",
            DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn).ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
