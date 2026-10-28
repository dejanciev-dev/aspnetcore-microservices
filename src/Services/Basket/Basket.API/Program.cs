using Basket.API.Clients;
using Basket.API.Data;
using Basket.API.Endpoints;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using Basket.API.Events;

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

// With RabbitMQ: asynchronous checkout (publish BasketCheckedOut) and repricing on ProductPriceChanged.
// Without it: checkout calls Ordering.API synchronously over HTTP.
if (builder.AddRabbitMqMessaging(queuePrefix: "basket-api"))
{
    builder.Services.AddSingleton<IOrderingClient, MessagingOrderingClient>();
    builder.Services.AddEventHandler<ProductPriceChanged, ProductPriceChangedHandler>();
}
else
{
    builder.Services.AddHttpClient<IOrderingClient, OrderingClient>(http =>
        http.BaseAddress = new Uri(builder.Configuration["Services:Ordering"]!));
}

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapBasketEndpoints();

app.Run();
