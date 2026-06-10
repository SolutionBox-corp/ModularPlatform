using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;
using ModularPlatform.Billing.Messaging;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using ModularPlatform.Telemetry;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Stripe.ReconcileStripe;

/// <summary>
/// Stripe reconcile sweep. Two passes per run, both capped to avoid runaway behaviour:
/// <list type="number">
/// <item>
/// <b>Stuck events</b> — <c>stripe_events</c> rows with <c>ProcessedAt IS NULL</c> and
/// <c>ReceivedAt &lt; now − 30 min</c>. Re-publishes <see cref="ProcessStripeEventMessage"/> via the outbox
/// so Wolverine's retry/DLQ machinery picks them back up. The router is idempotent. Cap: 200 per run.
/// </item>
/// <item>
/// <b>Subscription drift</b> — local non-Canceled <c>stripe_subscriptions</c> rows. Compares each against
/// the live Stripe API (<see cref="IStripeGateway"/>); on drift dispatches
/// <see cref="UpsertSubscriptionFromStripeCommand"/> and increments the <c>platform.billing.stripe_drift</c>
/// counter. Cap: 500 per run.
/// </item>
/// </list>
/// </summary>
internal sealed class ReconcileStripeHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IDispatcher dispatcher,
    IStripeGateway stripeGateway,
    IClock clock,
    ILogger<ReconcileStripeHandler> logger)
    : ICommandHandler<ReconcileStripeCommand, ReconcileStripeResponse>
{
    private const int StuckEventCap = 200;
    private const int SubscriptionCap = 500;
    private static readonly System.Diagnostics.Metrics.Counter<long> DriftCounter =
        PlatformMetrics.Meter.CreateCounter<long>(
            "platform.billing.stripe_drift",
            description: "Number of local Stripe subscription rows corrected by the reconcile sweep.");

    public async Task<ReconcileStripeResponse> Handle(ReconcileStripeCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var now = clock.UtcNow;
        var staleThreshold = now.AddMinutes(-30);

        // --- Pass 1: Stuck stripe events ---
        var stuckEvents = await db.StripeEvents
            .Where(e => e.ProcessedAt == null && e.ReceivedAt < staleThreshold)
            .OrderBy(e => e.ReceivedAt)
            .Take(StuckEventCap)
            .ToListAsync(ct);

        if (stuckEvents.Count == StuckEventCap)
        {
            logger.LogWarning(
                "Stripe reconcile: stuck-event cap ({Cap}) reached — there may be more unprocessed events",
                StuckEventCap);
        }

        foreach (var ev in stuckEvents)
        {
            await outbox.PublishAsync(new ProcessStripeEventMessage(ev.StripeEventId, ev.Type));
        }

        if (stuckEvents.Count > 0)
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }

        // --- Pass 2: Subscription drift ---
        var localSubscriptions = await db.Subscriptions
            .Where(s => s.Status != Entities.SubscriptionStatus.Canceled)
            .OrderBy(s => s.UpdatedAt)
            .Take(SubscriptionCap)
            .ToListAsync(ct);

        if (localSubscriptions.Count == SubscriptionCap)
        {
            logger.LogWarning(
                "Stripe reconcile: subscription cap ({Cap}) reached — some subscriptions may not have been checked",
                SubscriptionCap);
        }

        var driftCount = 0;
        foreach (var local in localSubscriptions)
        {
            var live = await stripeGateway.GetSubscriptionAsync(local.StripeSubscriptionId, ct);
            if (live is null)
            {
                continue; // Unknown in Stripe (or API hiccup) — skip; the next run retries.
            }

            var hasDrift = local.Status != UpsertSubscriptionFromStripeHandler.MapStatus(live.Status)
                           || local.CurrentPeriodEnd != live.CurrentPeriodEnd
                           || local.CancelAtPeriodEnd != live.CancelAtPeriodEnd;

            if (!hasDrift)
            {
                continue;
            }

            logger.LogWarning(
                "Stripe subscription drift detected for {SubscriptionId} (user {UserId}): " +
                "local status={LocalStatus}, live status={LiveStatus}",
                local.StripeSubscriptionId, local.UserId, local.Status, live.Status);

            DriftCounter.Add(1);
            driftCount++;

            // Stripe wins: the upsert mirrors live OBJECT state (same path the webhooks use).
            await dispatcher.Send(new UpsertSubscriptionFromStripeCommand(local.StripeSubscriptionId), ct);
        }

        return new ReconcileStripeResponse(stuckEvents.Count, driftCount);
    }
}
