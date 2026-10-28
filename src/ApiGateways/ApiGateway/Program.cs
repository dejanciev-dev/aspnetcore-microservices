using ApiGateway;
using BuildingBlocks.Common;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureOpenTelemetry();
builder.Services.AddHealthChecks();
builder.AddBff();
builder.AddGatewayRateLimiting();
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(Bff.AddAccessTokenTransform);

var app = builder.Build();

app.UseForwardedHeaders();     // client IP for rate-limit partitions and redirect URIs
app.UseAuthentication();       // the user is known before rate limiting partitions by it
app.UseRateLimiter();
app.UseCsrfHeaderCheck();

app.MapHealthChecks("/health");
app.MapBffEndpoints();
app.MapReverseProxy();

app.Run();
