using ApiGateway;
using BuildingBlocks.Common;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureOpenTelemetry();
builder.Services.AddHealthChecks();
builder.AddBff();
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(Bff.AddAccessTokenTransform);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseAuthentication();
app.UseCsrfHeaderCheck();

app.MapHealthChecks("/health");
app.MapBffEndpoints();
app.MapReverseProxy();

app.Run();
