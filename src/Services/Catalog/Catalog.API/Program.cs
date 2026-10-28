using BuildingBlocks.Common;
using Catalog.API.Data;
using Catalog.API.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddValidation();
builder.Services.AddSingleton<IProductRepository, InMemoryProductRepository>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapProductEndpoints();

app.Run();
