using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Scalar.AspNetCore;

namespace BuildingBlocks.Common;

/// <summary>
/// Cross-cutting setup shared by every microservice: OpenAPI, ProblemDetails,
/// health checks and resilient outgoing HTTP calls.
/// </summary>
public static class ServiceDefaults
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();

        // Retries, circuit breaker and timeouts for every HttpClient created via IHttpClientFactory.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapOpenApi();
        app.MapScalarApiReference();
        app.MapHealthChecks("/health");

        return app;
    }
}
