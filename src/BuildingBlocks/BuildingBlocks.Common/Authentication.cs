using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Common;

public static class Authentication
{
    /// <summary>Audience every API expects; the Keycloak clients add it with an audience mapper.</summary>
    public const string ApiAudience = "storefront-api";

    public const string AdminPolicy = "admin";

    /// <summary>
    /// Validates Keycloak-issued JWT access tokens. Configuration (section <c>Identity</c>):
    /// <list type="bullet">
    /// <item><c>Authority</c>: realm URL, e.g. <c>http://localhost:8080/realms/storefront</c>.</item>
    /// <item><c>MetadataAddress</c> (optional): where to fetch discovery from when the service reaches
    /// Keycloak under a different host than the browser does (docker-compose).</item>
    /// </list>
    /// </summary>
    public static IHostApplicationBuilder AddApiAuthentication(this IHostApplicationBuilder builder)
    {
        var identity = builder.Configuration.GetSection("Identity");

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = identity["Authority"];
                if (identity["MetadataAddress"] is { Length: > 0 } metadata) options.MetadataAddress = metadata;
                options.Audience = ApiAudience;
                options.RequireHttpsMetadata = identity.GetValue("RequireHttpsMetadata", !builder.Environment.IsDevelopment());
                options.MapInboundClaims = false;   // keep the JWT claim names (sub, preferred_username, ...)
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = "role";
                options.Events = new JwtBearerEvents { OnTokenValidated = ctx => { AddKeycloakRealmRoles(ctx.Principal); return Task.CompletedTask; } };
            });

        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy => policy.RequireRole("admin"));

        return builder;
    }

    /// <summary>Keycloak puts realm roles in <c>{"realm_access":{"roles":[...]}}</c>; expose them as role claims.</summary>
    public static void AddKeycloakRealmRoles(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity) return;
        if (identity.FindFirst("realm_access")?.Value is not { } realmAccess) return;

        using var json = JsonDocument.Parse(realmAccess);
        if (!json.RootElement.TryGetProperty("roles", out var roles)) return;

        foreach (var role in roles.EnumerateArray().Select(r => r.GetString()).OfType<string>())
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
    }

    /// <summary>The authenticated user's name; endpoints using it require authorization, so it is always set.</summary>
    public static string UserName(this ClaimsPrincipal user) =>
        user.Identity?.Name ?? throw new InvalidOperationException("The request is not authenticated.");
}

/// <summary>
/// Forwards the incoming request's bearer token on outgoing service-to-service calls, so the
/// downstream service sees the same user ("token relay").
/// </summary>
public sealed class ForwardAccessTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (accessor.HttpContext?.Request.Headers.Authorization.ToString() is { Length: > 0 } header
            && AuthenticationHeaderValue.TryParse(header, out var value))
        {
            request.Headers.Authorization = value;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
