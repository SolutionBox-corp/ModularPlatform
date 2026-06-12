using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Auth.PurgeRefreshTokens;
using Quartz;

namespace ModularPlatform.Identity.Jobs;

/// <summary>Thin cron adapter (canonical shape): dispatches <see cref="PurgeRefreshTokensCommand"/>. Runs in the Jobs
/// host; <see cref="DisallowConcurrentExecutionAttribute"/> prevents overlap.</summary>
[DisallowConcurrentExecution]
internal sealed class IdentityPurgeRefreshTokensJob(IDispatcher dispatcher) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await dispatcher.Send(new PurgeRefreshTokensCommand(), context.CancellationToken);
    }
}
