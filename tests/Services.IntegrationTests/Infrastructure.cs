using Testcontainers.PostgreSql;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Services.IntegrationTests.Infrastructure))]

namespace Services.IntegrationTests;

/// <summary>
/// One PostgreSQL and one Redis container shared by the whole test run.
/// Without Docker the tests are skipped locally, but they fail on CI so a broken
/// runner never passes silently.
/// </summary>
public sealed class Infrastructure : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Build() already probes the Docker endpoint, so it belongs inside the try.
            _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
            _redis = new RedisBuilder("redis:8-alpine").Build();
            await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
        }
        catch (Exception ex) when (Environment.GetEnvironmentVariable("CI") is null)
        {
            UnavailableReason = $"Docker is not available ({ex.GetType().Name}); integration tests need it.";
        }
    }

    /// <summary>Tests pass their own database name, so they never see each other's data.</summary>
    public string PostgresConnectionString(string database)
    {
        Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? "");
        return $"{_postgres!.GetConnectionString()};Database={database}";
    }

    public string RedisConnectionString
    {
        get
        {
            Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? "");
            return _redis!.GetConnectionString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
    }
}
