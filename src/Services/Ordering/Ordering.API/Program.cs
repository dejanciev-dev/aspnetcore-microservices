using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Ordering.API.Data;
using Ordering.API.Endpoints;
using Ordering.API.Events;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);

// PostgreSQL when a connection string is configured (Aspire, docker-compose); in-memory otherwise.
var usePostgres = builder.Configuration.GetConnectionString(OrderingDbContext.ConnectionName) is not null;
if (usePostgres)
{
    // Aspire integration: pooled DbContext, retries, health check and OpenTelemetry tracing.
    builder.AddNpgsqlDbContext<OrderingDbContext>(OrderingDbContext.ConnectionName);
    builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
    builder.Services.AddHealthChecks().AddPendingMigrationsCheck<OrderingDbContext>();
}
else
{
    builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
}

// RabbitMQ when configured: orders are created from BasketCheckedOut events (POST /api/orders keeps working).
if (builder.AddRabbitMqMessaging(queuePrefix: "ordering-api"))
{
    builder.Services.AddEventHandler<BasketCheckedOut, BasketCheckedOutHandler>();
}

var app = builder.Build();

// Migrations at startup by default; `migrate` applies them and exits (a Kubernetes Job), and
// Database:MigrateOnStartup=false leaves them to that job when there are several replicas.
if (usePostgres && await app.ApplyMigrationsAsync<OrderingDbContext>(args)) return;

app.MapDefaultEndpoints();
app.MapOrderEndpoints();

app.Run();
