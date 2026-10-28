using BuildingBlocks.Common;
using Ordering.API.Data;
using Ordering.API.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapOrderEndpoints();

app.Run();
