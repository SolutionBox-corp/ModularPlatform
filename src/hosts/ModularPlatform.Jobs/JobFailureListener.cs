using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using ModularPlatform.Telemetry;
using Quartz;

namespace ModularPlatform.Jobs;

/// <summary>
/// Centralised job-failure observability. A throwing Quartz job is otherwise SILENT — Quartz logs internally and
/// waits for the next cron fire (hours later for reconcile/retention), so a persistently failing job goes
/// unnoticed. This single listener (registered for ALL job groups) emits a structured ERROR and increments the
/// <c>platform.jobs.failures</c> counter on every failed execution, regardless of which module owns the job.
/// </summary>
public static class JobFailureMetrics
{
    private static readonly Counter<long> Failures = PlatformMetrics.Meter.CreateCounter<long>(
        "platform.jobs.failures",
        description: "Number of scheduled-job executions that threw an exception.");

    public static void RecordFailure(string jobName) =>
        Failures.Add(1, new KeyValuePair<string, object?>("job", jobName));
}

public sealed class JobFailureListener(ILogger<JobFailureListener> logger) : IJobListener
{
    public string Name => "platform-job-failure-listener";

    public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task JobWasExecuted(
        IJobExecutionContext context, JobExecutionException? jobException, CancellationToken ct = default)
    {
        if (jobException is not null)
        {
            var jobName = context.JobDetail.Key.Name;
            JobFailureMetrics.RecordFailure(jobName);
            logger.LogError(jobException,
                "Scheduled job {JobName} failed — it will not retry until its next cron fire; investigate", jobName);
        }

        return Task.CompletedTask;
    }
}
