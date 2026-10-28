using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
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

if (usePostgres)
{
    // Demo convenience: apply migrations at startup. In production run them as a deployment step.
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderingDbContext>().Database.MigrateAsync();
}

app.MapDefaultEndpoints();
app.MapOrderEndpoints();

app.Run();
