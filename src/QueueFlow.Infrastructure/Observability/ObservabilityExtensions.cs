using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using QueueFlow.Application.Observability;

namespace QueueFlow.Infrastructure.Observability;

public static class ObservabilityExtensions
{
    public static IServiceCollection AddQueueFlowObservability(this IServiceCollection services, IConfiguration configuration, string serviceName, bool instrumentAspNetCore = false)
    {
        var openTelemetry = services.AddOpenTelemetry().ConfigureResource(resource => resource
            .AddService(serviceName, serviceVersion: typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString())
            .AddAttributes([new("deployment.environment", configuration["ASPNETCORE_ENVIRONMENT"] ?? configuration["DOTNET_ENVIRONMENT"] ?? "Production")]));

        openTelemetry.WithTracing(tracing =>
        {
            tracing.AddSource(QueueFlowTelemetry.Name).AddSource("Npgsql").AddHttpClientInstrumentation();
            if (instrumentAspNetCore) tracing.AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"));
            if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) tracing.AddOtlpExporter();
        });

        openTelemetry.WithMetrics(metrics =>
        {
            metrics.AddMeter(QueueFlowTelemetry.Name).AddRuntimeInstrumentation().AddHttpClientInstrumentation();
            if (instrumentAspNetCore) metrics.AddAspNetCoreInstrumentation();
            if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) metrics.AddOtlpExporter();
        });

        return services;
    }
}
