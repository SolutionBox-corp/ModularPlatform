using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Stripe.ReconcileStripe;

/// <summary>
/// Reconciliation sweep for Stripe integration. Dispatched by the Jobs host on a cron (every 6 hours).
/// Idempotent; safe to double-fire under horizontal scale (all mutations go through EF xmin concurrency).
/// </summary>
public sealed record ReconcileStripeCommand : ICommand<ReconcileStripeResponse>;

/// <summary>Counts of items repaired by a single reconcile run.</summary>
public sealed record ReconcileStripeResponse(
    int StuckEventsRequeued,
    int SubscriptionDriftsFixed);
