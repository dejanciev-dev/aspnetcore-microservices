using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ApiGateway;

/// <summary>
/// Rate limiting at the edge, so every service behind the gateway is protected in one place.
/// <list type="bullet">
/// <item>A <b>global</b> token bucket per caller on all traffic except health checks: signed-in
/// users are partitioned by user name (same budget across devices), anonymous callers by IP.</item>
/// <item>Stricter <b>named policies</b> for expensive or abuse-prone endpoints: checkout (YARP route
/// metadata) and starting a sign-in.</item>
/// </list>
/// Rejected requests get <c>429</c> with ProblemDetails and <c>Retry-After</c>. Limits come from
/// configuration (<c>RateLimiting:*</c>).
/// </summary>
internal static class RateLimiting
{
    public const string CheckoutPolicy = "checkout";
    public const string LoginPolicy = "login";

    public static WebApplicationBuilder AddGatewayRateLimiting(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection("RateLimiting").Get<RateLimitSettings>() ?? new RateLimitSettings();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = OnRejectedAsync;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (context.Request.Path.StartsWithSegments("/health"))
                    return RateLimitPartition.GetNoLimiter("health");

                var key = PartitionKey(context);
                var bucket = key.StartsWith("user:", StringComparison.Ordinal) ? settings.Authenticated : settings.Anonymous;
                return RateLimitPartition.GetTokenBucketLimiter(key, _ => bucket.ToOptions());
            });

            options.AddPolicy(CheckoutPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context), _ => settings.Checkout.ToOptions()));

            options.AddPolicy(LoginPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                PartitionKey(context), _ => settings.Login.ToOptions()));
        });

        return builder;
    }

    /// <summary>
    /// "user:{name}" for signed-in users, otherwise "ip:{address}". The address is the client's,
    /// not the proxy's, because UseForwardedHeaders runs first.
    /// </summary>
    internal static string PartitionKey(HttpContext context) =>
        context.User.Identity is { IsAuthenticated: true, Name: { Length: > 0 } name }
            ? $"user:{name}"
            : $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : (TimeSpan?)null;
        if (retryAfter is { } delay)
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(RateLimiting))
            .LogWarning("Rate limit hit by {Partition} on {Method} {Path}", PartitionKey(http), http.Request.Method, http.Request.Path);

        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                detail: retryAfter is { } d ? $"Try again in {Math.Max(1, (int)Math.Ceiling(d.TotalSeconds))} seconds." : "Try again later.")
            .ExecuteAsync(http);
    }
}

internal sealed class RateLimitSettings
{
    public TokenBucketSettings Anonymous { get; set; } = new() { TokenLimit = 60, TokensPerPeriod = 30, ReplenishmentPeriodSeconds = 10 };
    public TokenBucketSettings Authenticated { get; set; } = new() { TokenLimit = 200, TokensPerPeriod = 100, ReplenishmentPeriodSeconds = 10 };
    public FixedWindowSettings Checkout { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public FixedWindowSettings Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
}

/// <summary>Allows bursts up to <see cref="TokenLimit"/>, then <see cref="TokensPerPeriod"/> per period.</summary>
internal sealed class TokenBucketSettings
{
    public int TokenLimit { get; set; }
    public int TokensPerPeriod { get; set; }
    public int ReplenishmentPeriodSeconds { get; set; }

    public TokenBucketRateLimiterOptions ToOptions() => new()
    {
        TokenLimit = TokenLimit,
        TokensPerPeriod = TokensPerPeriod,
        ReplenishmentPeriod = TimeSpan.FromSeconds(ReplenishmentPeriodSeconds),
        QueueLimit = 0,   // reject straight away instead of holding requests open
        AutoReplenishment = true,
    };
}

internal sealed class FixedWindowSettings
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }

    public FixedWindowRateLimiterOptions ToOptions() => new()
    {
        PermitLimit = PermitLimit,
        Window = TimeSpan.FromSeconds(WindowSeconds),
        QueueLimit = 0,
        AutoReplenishment = true,
    };
}
