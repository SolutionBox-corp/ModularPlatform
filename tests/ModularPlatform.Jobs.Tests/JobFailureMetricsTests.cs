using System.Diagnostics.Metrics;
using ModularPlatform.Jobs;
using Shouldly;

namespace ModularPlatform.Jobs.Tests;

/// <summary>
/// A throwing scheduled job must surface a failure SIGNAL (not just an internal Quartz log) so a persistently
/// failing reconcile/retention/expiry job is alertable. The signal is the <c>platform.jobs.failures</c> counter.
/// </summary>
public sealed class JobFailureMetricsTests
{
    [Fact]
    public void Recording_a_failure_increments_the_platform_jobs_failures_counter()
    {
        long recorded = 0;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "ModularPlatform" && instrument.Name == "platform.jobs.failures")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref recorded, value));
        listener.Start();

        JobFailureMetrics.RecordFailure("billing-stripe-reconcile");

        Interlocked.Read(ref recorded).ShouldBe(1);
    }
}
