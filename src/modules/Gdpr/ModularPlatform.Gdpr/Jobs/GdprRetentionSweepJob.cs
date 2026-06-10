using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Features.Retention.RetentionSweep;
using Quartz;

namespace ModularPlatform.Gdpr.Jobs;

/// <summary>
/// Thin Quartz job that dispatches the GDPR retention sweep nightly (default 03:00 UTC, configurable via
/// <c>Modules:Gdpr:Jobs:RetentionSweepCron</c>). All logic lives in <see cref="RetentionSweepHandler"/>.
/// <see cref="DisallowConcurrentExecutionAttribute"/> prevents a second sweep starting before the first finishes.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class GdprRetentionSweepJob(IDispatcher dispatcher) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await dispatcher.Send(new RetentionSweepCommand(), context.CancellationToken);
    }
}
