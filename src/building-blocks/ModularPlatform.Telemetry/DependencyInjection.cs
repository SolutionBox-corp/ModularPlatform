using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Cqrs;
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
    public static IServiceCollection AddPlatformTelemetry(this IServiceCollection services, string serviceName)
    {
        services.AddPipelineBehavior(typeof(TelemetryBehavior<,>));

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
}
