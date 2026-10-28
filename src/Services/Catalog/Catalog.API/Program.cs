using BuildingBlocks.Common;
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
}
else
{
    builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();
}

var app = builder.Build();

if (usePostgres)
{
    // Demo convenience: apply migrations (and seed) at startup. In production run them as a deployment step.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();
app.MapProductEndpoints();

app.Run();
