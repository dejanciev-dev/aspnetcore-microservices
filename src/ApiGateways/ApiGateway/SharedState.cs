using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;
using Microsoft.AspNetCore.OutputCaching.StackExchangeRedis;
using StackExchange.Redis;

namespace ApiGateway;

/// <summary>
/// What gateway replicas must share to be interchangeable. With <c>ConnectionStrings:redis</c> set:
/// <list type="bullet">
/// <item><b>Data Protection keys</b>: the session cookie and the OIDC correlation and nonce cookies
/// are encrypted with them, so a sign-in started on one replica can finish on another, and sessions
/// survive restarts.</item>
/// <item><b>Output cache</b>: entries and tags live in Redis, so a catalog write through any replica
/// evicts the cached reads of all of them.</item>
/// </list>
/// Without Redis both stay in memory, which is fine for a single instance (dotnet run, Compose).
/// Rate-limit counters stay per instance either way: N replicas allow N times the configured rate.
/// </summary>
internal static class SharedState
{
    public const string ConnectionName = "redis";
    public const string DataProtectionKeys = "storefront:gateway:data-protection-keys";
    public const string OutputCachePrefix = "storefront:gateway:output-cache:";

    public static WebApplicationBuilder AddSharedState(this WebApplicationBuilder builder)
    {
        // A fixed name, so every replica (whatever its content root) derives the same key purposes.
        builder.Services.AddDataProtection().SetApplicationName("storefront-gateway");

        if (builder.Configuration.GetConnectionString(ConnectionName) is null) return builder;

        // Aspire integration: one shared IConnectionMultiplexer with health check and tracing.
        builder.AddRedisClient(ConnectionName);

        builder.Services.AddOptions<KeyManagementOptions>()
            .Configure<IServiceProvider>((options, services) => options.XmlRepository = new RedisXmlRepository(
                () => services.GetRequiredService<IConnectionMultiplexer>().GetDatabase(), DataProtectionKeys));

        builder.Services.AddStackExchangeRedisOutputCache(options => options.InstanceName = OutputCachePrefix);
        builder.Services.AddOptions<RedisOutputCacheOptions>()
            .Configure<IServiceProvider>((options, services) => options.ConnectionMultiplexerFactory =
                () => Task.FromResult(services.GetRequiredService<IConnectionMultiplexer>()));

        return builder;
    }
}
