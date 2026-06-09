using ModularPlatform.Billing.Features.Credits.ExpireCredits;
using ModularPlatform.Cqrs;
using Quartz;

namespace ModularPlatform.Billing.Jobs;

/// <summary>
/// CANONICAL cron job: a thin <see cref="IJob"/> that just dispatches a command. All logic lives in the
/// handler (<see cref="ExpireCreditsHandler"/>); the job is the scheduler adapter. Runs in the Jobs host under
/// system context (sweeps every account). <see cref="DisallowConcurrentExecutionAttribute"/> prevents overlap.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class BillingExpireCreditsJob(IDispatcher dispatcher) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await dispatcher.Send(new ExpireCreditsCommand(), context.CancellationToken);
    }
}
