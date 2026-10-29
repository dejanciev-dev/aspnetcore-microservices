using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Catalog.API.Data;
using Catalog.API.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddValidation();

// PostgreSQL when a connection string is configured (Aspire, docker-compose); in-memory otherwise.
var usePostgres = builder.Configuration.GetConnectionString(CatalogDbContext.ConnectionName) is not null;
if (usePostgres)
{
    // Aspire integration: pooled DbContext, retries, health check and OpenTelemetry tracing.
    builder.AddNpgsqlDbContext<CatalogDbContext>(CatalogDbContext.ConnectionName, configureDbContextOptions: options =>
        options.UseAsyncSeeding((db, _, ct) => CatalogDbContext.SeedAsync(db, ct)));
    builder.Services.AddScoped<IProductRepository, EfProductRepository>();
    builder.Services.AddHealthChecks().AddPendingMigrationsCheck<CatalogDbContext>();
}
else
{
    builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
}

// RabbitMQ when configured: the outbox relay publishes ProductPriceChanged events staged by
// EfProductRepository. Without a broker the outbox simply fills up and is drained once one is added.
if (builder.AddRabbitMqMessaging(queuePrefix: "catalog-api") && usePostgres)
{
    builder.Services.AddOutboxRelay<CatalogDbContext>();
}

var app = builder.Build();

// Migrations (and the seed) at startup by default; `migrate` applies them and exits (a Kubernetes
// Job), and Database:MigrateOnStartup=false leaves them to that job when there are several replicas.
if (usePostgres && await app.ApplyMigrationsAsync<CatalogDbContext>(args)) return;

app.MapDefaultEndpoints();
app.MapProductEndpoints();

app.Run();
