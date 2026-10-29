using Microsoft.AspNetCore.OutputCaching;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace ApiGateway;

/// <summary>
/// Output caching at the edge for public, read-heavy routes, so repeated catalog reads never reach
/// Catalog.API or PostgreSQL.
/// <list type="bullet">
/// <item>A route opts in with YARP's <c>"OutputCachePolicy"</c>; the policy name doubles as the cache tag.</item>
/// <item>Only <c>GET 200</c> responses without <c>Set-Cookie</c> are stored, keyed by path and query string.
/// Unlike the framework default, signed-in users share the entries: catalog data is the same for everyone.</item>
/// <item>A successful write (POST/PUT/PATCH/DELETE) through the same route evicts the tag, so admins see
/// their changes straight away; the expiry bounds staleness for changes that bypass the gateway.</item>
/// </list>
/// Durations come from configuration (<c>OutputCaching:*</c>).
/// </summary>
internal static class OutputCaching
{
    public const string CatalogPolicy = "catalog";

    public static WebApplicationBuilder AddGatewayOutputCaching(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("OutputCaching").Get<OutputCacheSettings>() ?? new OutputCacheSettings();

        builder.Services.AddOutputCache(options =>
            options.AddPolicy(CatalogPolicy, new PublicReadPolicy(CatalogPolicy, TimeSpan.FromSeconds(settings.CatalogSeconds))));

        return builder;
    }

    /// <summary>Evicts a cached route's entries once a write through that route has succeeded downstream.</summary>
    public static void AddEvictionTransform(TransformBuilderContext context)
    {
        if (context.Route.OutputCachePolicy is not { Length: > 0 } tag) return;

        context.AddResponseTransform(async transform =>
        {
            var method = transform.HttpContext.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return;
            if (transform.ProxyResponse is not { IsSuccessStatusCode: true }) return;

            // Not RequestAborted: the write happened, so the stale entries must go even if the caller left.
            await transform.HttpContext.RequestServices.GetRequiredService<IOutputCacheStore>()
                .EvictByTagAsync(tag, CancellationToken.None);
        });
    }

    private sealed class PublicReadPolicy(string tag, TimeSpan duration) : IOutputCachePolicy
    {
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            var cacheable = HttpMethods.IsGet(context.HttpContext.Request.Method);

            context.EnableOutputCaching = true;
            context.AllowCacheLookup = cacheable;
            context.AllowCacheStorage = cacheable;
            context.AllowLocking = true;   // concurrent misses for one key wait for a single downstream call
            context.ResponseExpirationTimeSpan = duration;
            context.CacheVaryByRules.QueryKeys = "*";
            context.Tags.Add(tag);
            return ValueTask.CompletedTask;
        }

        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation) => ValueTask.CompletedTask;

        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            var response = context.HttpContext.Response;

            // Errors and 404s stay uncached, and a refreshed session cookie must never be replayed to someone else.
            if (response.StatusCode != StatusCodes.Status200OK || response.Headers.SetCookie.Count > 0)
                context.AllowCacheStorage = false;

            return ValueTask.CompletedTask;
        }
    }
}

internal sealed class OutputCacheSettings
{
    public int CatalogSeconds { get; set; } = 60;
}
