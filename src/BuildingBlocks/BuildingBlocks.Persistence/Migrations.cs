using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.Persistence;

/// <summary>
/// When and where EF Core migrations run for a service that owns a relational database.
/// <list type="bullet">
/// <item>Default: at startup, which suits one instance (dotnet run, Aspire, Docker Compose).</item>
/// <item><c>migrate</c> on the command line: apply migrations (and the seed) and exit, for a
/// Kubernetes Job or a deployment step.</item>
/// <item><c>Database:MigrateOnStartup=false</c>: never at startup, so several replicas don't race;
/// the readiness check then keeps them out of rotation until the migration job has run.</item>
/// </list>
/// </summary>
public static class Migrations
{
    public const string Command = "migrate";

    /// <summary>Readiness fails while the database has migrations the code expects but lacks.</summary>
    public static IHealthChecksBuilder AddPendingMigrationsCheck<TContext>(this IHealthChecksBuilder checks) where TContext : DbContext
    {
        checks.Services.AddSingleton<SchemaState<TContext>>();
        return checks.AddCheck<PendingMigrationsCheck<TContext>>($"{typeof(TContext).Name} schema");
    }

    /// <summary>Applies migrations as configured; returns <c>true</c> when the process should exit.</summary>
    public static async Task<bool> ApplyMigrationsAsync<TContext>(this WebApplication app, string[] args) where TContext : DbContext
    {
        var migrateAndExit = args.Contains(Command);
        if (!migrateAndExit && !app.Configuration.GetValue("Database:MigrateOnStartup", true)) return false;

        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();
        return migrateAndExit;
    }
}

/// <summary>Applied migrations never become pending again, so the check stops asking once current.</summary>
internal sealed class SchemaState<TContext> where TContext : DbContext
{
    public volatile bool UpToDate;
}

internal sealed class PendingMigrationsCheck<TContext>(TContext db, SchemaState<TContext> state) : IHealthCheck where TContext : DbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (state.UpToDate) return HealthCheckResult.Healthy();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
            return HealthCheckResult.Unhealthy($"Waiting for {pending.Count} migration(s): {string.Join(", ", pending)}");

        state.UpToDate = true;
        return HealthCheckResult.Healthy();
    }
}
