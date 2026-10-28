using Basket.API.Clients;
using Basket.API.Data;
using Basket.API.Endpoints;
using BuildingBlocks.Common;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddValidation();

// Redis when a connection string is configured (Aspire, docker-compose); in-memory otherwise.
if (builder.Configuration.GetConnectionString("redis") is not null)
{
    // Aspire integration: registers IConnectionMultiplexer with health check and OpenTelemetry tracing.
    builder.AddRedisClient("redis");
    builder.Services.AddSingleton<IBasketRepository, RedisBasketRepository>();
}
else
{
    builder.Services.AddSingleton<IBasketRepository, InMemoryBasketRepository>();
}

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(http =>
    http.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]!));

builder.Services.AddHttpClient<IOrderingClient, OrderingClient>(http =>
    http.BaseAddress = new Uri(builder.Configuration["Services:Ordering"]!));

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapBasketEndpoints();

app.Run();
