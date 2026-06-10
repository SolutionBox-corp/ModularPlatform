using System.Diagnostics.Metrics;

namespace ModularPlatform.Telemetry;

/// <summary>
/// Platform-wide <see cref="Meter"/> and instrument factory. Modules record counters/gauges against this
/// shared meter so all ops metrics land under a single <c>ModularPlatform</c> scope in any OTel backend.
/// Mirrors <see cref="PlatformTelemetry"/> (which owns the <see cref="System.Diagnostics.ActivitySource"/>).
/// </summary>
public static class PlatformMetrics
{
    /// <summary>The meter name — also registered via <see cref="TelemetryServiceCollectionExtensions.AddPlatformTelemetry"/>.</summary>
    public const string MeterName = "ModularPlatform";

    /// <summary>
    /// Shared <see cref="Meter"/> instance. Modules call <c>PlatformMetrics.Meter.CreateCounter&lt;T&gt;(...)</c>
    /// or <c>CreateObservableGauge</c> to record operational signals.
    /// </summary>
    public static readonly Meter Meter = new(MeterName);
}
