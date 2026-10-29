using ApiGateway;
using BuildingBlocks.Common;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureOpenTelemetry();
builder.Services.AddHealthChecks();
builder.AddBff();
builder.AddGatewayRateLimiting();
builder.AddGatewayOutputCaching();
builder.Services
    .AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(Bff.AddAccessTokenTransform)
    .AddTransforms(OutputCaching.AddEvictionTransform);

var app = builder.Build();

app.UseForwardedHeaders();     // client IP for rate-limit partitions and redirect URIs
app.UseAuthentication();       // the user is known before rate limiting partitions by it
app.UseRateLimiter();
app.UseCsrfHeaderCheck();
app.UseOutputCache();          // after rate limiting and the CSRF check, so cache hits still pass both

app.MapHealthEndpoints();
app.MapBffEndpoints();
app.MapReverseProxy();

app.Run();
