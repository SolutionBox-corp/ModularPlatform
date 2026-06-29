using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using ModularPlatform.Jobs;
using Quartz;
using Quartz.Impl;
using Quartz.Spi;
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

    [Fact]
    public async Task JobWasExecuted_with_exception_logs_and_records_the_failed_job()
    {
        long recorded = 0;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == "ModularPlatform" && instrument.Name == "platform.jobs.failures")
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, _, _) => Interlocked.Add(ref recorded, value));
        meterListener.Start();

        var logger = new CapturingLogger<JobFailureListener>();
        var listener = new JobFailureListener(logger);
        var context = NewExecutionContext("billing-stripe-reconcile");

        await listener.JobWasExecuted(context, new JobExecutionException("boom"));

        Interlocked.Read(ref recorded).ShouldBe(1);
        logger.Entries.ShouldHaveSingleItem();
        logger.Entries[0].Level.ShouldBe(LogLevel.Error);
        logger.Entries[0].Message.ShouldContain("billing-stripe-reconcile");
    }

    private static JobExecutionContextImpl NewExecutionContext(string jobName)
    {
        var jobDetail = JobBuilder.Create<NoopJob>()
            .WithIdentity(jobName)
            .Build();
        var trigger = (IOperableTrigger)TriggerBuilder.Create()
            .WithIdentity($"{jobName}-trigger")
            .StartNow()
            .Build();
        var now = DateTimeOffset.UtcNow;
        var bundle = new TriggerFiredBundle(jobDetail, trigger, null, false, now, now, null, null);

        return new JobExecutionContextImpl(null!, bundle, new NoopJob());
    }

    private sealed class NoopJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
