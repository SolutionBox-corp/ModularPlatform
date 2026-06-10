using ModularPlatform.Billing.Features.Stripe.ReconcileStripe;
using ModularPlatform.Cqrs;
using Quartz;

namespace ModularPlatform.Billing.Jobs;

/// <summary>
/// Thin Quartz job that dispatches the Stripe reconcile sweep every 6 hours (configurable via
/// <c>Modules:Billing:Jobs:ReconcileStripeCron</c>). All logic lives in
/// <see cref="ReconcileStripeHandler"/>; this is the scheduler adapter only.
/// <see cref="DisallowConcurrentExecutionAttribute"/> prevents overlapping reconcile runs if a single sweep
/// takes longer than the interval.
/// </summary>
[DisallowConcurrentExecution]
internal sealed class BillingStripeReconcileJob(IDispatcher dispatcher) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        await dispatcher.Send(new ReconcileStripeCommand(), context.CancellationToken);
    }
}
