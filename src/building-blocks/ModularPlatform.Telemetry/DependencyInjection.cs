using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Cqrs;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ModularPlatform.Telemetry;

public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OTel tracing/metrics pipeline and the outer-most <see cref="TelemetryBehavior{TRequest,TResponse}"/>.
    /// MUST be called before AddPlatformWeb so the telemetry behavior is the first registered (outer-most).
    /// </summary>
    public static IServiceCollection AddPlatformTelemetry(
        this IServiceCollection services,
        string serviceName,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ValidateOtlpEndpointConfiguration(configuration, environment);

        services.AddPipelineBehavior(typeof(TelemetryBehavior<,>));
        services.Configure<OtlpExporterOptions>(configuration.GetSection("OpenTelemetry:Otlp"));

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(PlatformTelemetry.SourceName)
                .AddAspNetCoreInstrumentation()
                .AddOtlpExporter())
            .WithMetrics(m => m
                .AddMeter(PlatformMetrics.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter());

        return services;
    }

    private static void ValidateOtlpEndpointConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return;
        }

        var commonEndpoint = configuration["OpenTelemetry:Otlp:Endpoint"]
            ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(commonEndpoint))
        {
            return;
        }

        var tracesEndpoint = configuration["OTEL_EXPORTER_OTLP_TRACES_ENDPOINT"];
        var metricsEndpoint = configuration["OTEL_EXPORTER_OTLP_METRICS_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(tracesEndpoint) && !string.IsNullOrWhiteSpace(metricsEndpoint))
        {
            return;
        }

        throw new InvalidOperationException(
            "OpenTelemetry OTLP endpoint must be configured outside Development/Testing. "
            + "Set OpenTelemetry:Otlp:Endpoint or OTEL_EXPORTER_OTLP_ENDPOINT, "
            + "or set both OTEL_EXPORTER_OTLP_TRACES_ENDPOINT and OTEL_EXPORTER_OTLP_METRICS_ENDPOINT.");
    }
}
