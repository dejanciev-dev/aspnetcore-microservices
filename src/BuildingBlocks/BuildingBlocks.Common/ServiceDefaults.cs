using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

namespace BuildingBlocks.Common;

/// <summary>
/// Cross-cutting setup shared by every microservice: OpenAPI, ProblemDetails,
/// health checks, resilient outgoing HTTP calls, OpenTelemetry and JWT authentication.
/// </summary>
public static class ServiceDefaults
{
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        builder.ConfigureOpenTelemetry();
        builder.AddApiAuthentication();

        builder.Services.AddOpenApi();
        builder.Services.AddProblemDetails();
        builder.Services.AddHealthChecks();

        // Retries, circuit breaker and timeouts for every HttpClient created via IHttpClientFactory.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        return builder;
    }

    /// <summary>
    /// Logs, metrics and distributed traces, exported only where a destination is configured (so
    /// tests stay silent): over OTLP when OTEL_EXPORTER_OTLP_ENDPOINT is set (Aspire, docker-compose,
    /// Kubernetes), and to Application Insights when APPLICATIONINSIGHTS_CONNECTION_STRING is set
    /// (Azure Container Apps).
    /// </summary>
    public static IHostApplicationBuilder ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Microsoft.AspNetCore.RateLimiting"))   // leases, queueing and rejections
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource("BuildingBlocks.Messaging")   // RabbitMQ publish/process spans
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation());

        // Probes arrive every few seconds per replica; keep them out of traces. PostConfigure wraps
        // whatever filter an exporter's own setup put there (the Azure Monitor distro sets one).
        builder.Services.PostConfigure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            var inner = options.Filter;
            options.Filter = ctx => !IsHealthProbe(ctx.Request.Path) && (inner?.Invoke(ctx) ?? true);
        });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        // Azure Monitor distro; it reads the connection string from configuration itself.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
            builder.Services.AddOpenTelemetry().UseAzureMonitor();

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapOpenApi();
        app.MapScalarApiReference();
        app.MapHealthEndpoints();

        return app;
    }

    /// <summary>
    /// <c>/health</c> runs every registered check (database, Redis, RabbitMQ): "ready for traffic".
    /// <c>/alive</c> runs none: "the process responds". Kubernetes uses /alive for liveness, so an
    /// outage of a dependency takes pods out of rotation instead of restarting them in a loop.
    /// </summary>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = _ => false });
        return app;
    }

    /// <summary>Probe traffic is excluded from traces and rate limits.</summary>
    public static bool IsHealthProbe(PathString path) =>
        path.StartsWithSegments("/health") || path.StartsWithSegments("/alive");
}
