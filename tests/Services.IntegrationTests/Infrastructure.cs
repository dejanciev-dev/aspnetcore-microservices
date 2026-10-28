using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Services.IntegrationTests.Infrastructure))]

namespace Services.IntegrationTests;

/// <summary>
/// One PostgreSQL, one Redis and one RabbitMQ container shared by the whole test run.
/// Without Docker the tests are skipped locally, but they fail on CI so a broken
/// runner never passes silently.
/// </summary>
public sealed class Infrastructure : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private RabbitMqContainer? _rabbitMq;

    public string? UnavailableReason { get; private set; }

    public async ValueTask InitializeAsync()
    {
        try
        {
            // Build() already probes the Docker endpoint, so it belongs inside the try.
            _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
            _redis = new RedisBuilder("redis:8-alpine").Build();
            _rabbitMq = new RabbitMqBuilder("rabbitmq:4-management-alpine").Build();
            await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync(), _rabbitMq.StartAsync());
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

    public string RabbitMqConnectionString
    {
        get
        {
            Assert.SkipWhen(UnavailableReason is not null, UnavailableReason ?? "");
            return _rabbitMq!.GetConnectionString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_postgres is not null) await _postgres.DisposeAsync();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_rabbitMq is not null) await _rabbitMq.DisposeAsync();
    }
}
