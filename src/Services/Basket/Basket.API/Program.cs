using Basket.API.Clients;
using Basket.API.Data;
using Basket.API.Endpoints;
using BuildingBlocks.Common;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddValidation();
builder.Services.AddSingleton<IBasketRepository, InMemoryBasketRepository>();

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(http =>
    http.BaseAddress = new Uri(builder.Configuration["Services:Catalog"]!));

builder.Services.AddHttpClient<IOrderingClient, OrderingClient>(http =>
    http.BaseAddress = new Uri(builder.Configuration["Services:Ordering"]!));

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapBasketEndpoints();

app.Run();
