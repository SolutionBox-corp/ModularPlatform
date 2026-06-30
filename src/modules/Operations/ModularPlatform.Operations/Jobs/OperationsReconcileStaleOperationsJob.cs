using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Features.ReconcileStaleOperations;
using Quartz;

namespace ModularPlatform.Operations.Jobs;

/// <summary>
/// Scheduler adapter for the operations stuck-state reconciliation sweep. Business logic lives in
/// <see cref="ReconcileStaleOperationsHandler"/>.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class OperationsReconcileStaleOperationsJob(IDispatcher dispatcher) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await dispatcher.Send(new ReconcileStaleOperationsCommand(), context.CancellationToken);
    }
}
