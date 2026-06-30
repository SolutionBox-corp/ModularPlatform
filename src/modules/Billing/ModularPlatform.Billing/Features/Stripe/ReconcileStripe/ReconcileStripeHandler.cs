using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;
using ModularPlatform.Billing.Messaging;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Sagas;
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
    private const int StuckPurchaseCap = 200;
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

        var stuckEventCapReached = stuckEvents.Count == StuckEventCap;
        if (stuckEventCapReached)
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

        var subscriptionCapReached = localSubscriptions.Count == SubscriptionCap;
        if (subscriptionCapReached)
        {
            logger.LogWarning(
                "Stripe reconcile: subscription cap ({Cap}) reached — some subscriptions may not have been checked",
                SubscriptionCap);
        }

        var driftCount = 0;
        foreach (var local in localSubscriptions)
        {
            // Per-item isolation: a non-404 Stripe error (429/500/timeout) on ONE subscription must not abort the
            // whole sweep — log it and move on; the next run retries this item.
            try
            {
                var live = await stripeGateway.GetSubscriptionAsync(local.StripeSubscriptionId, ct);
                if (live is null)
                {
                    continue; // Unknown in Stripe (or a 404 hiccup) — skip; the next run retries.
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
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Stripe reconcile: skipping subscription {SubscriptionId} after an error; will retry next run",
                    local.StripeSubscriptionId);
            }
        }

        // --- Pass 3: Stuck PAID purchases (dead-lettered confirmations) ---
        // A purchase whose CreditPurchaseConfirmed dead-lettered leaves the saga stuck Pending/Abandoned with the
        // money paid but no credits granted. Re-publish the confirmation ONLY when Stripe confirms the session was
        // actually paid — never grant an unpaid/abandoned purchase. The grant is idempotent (purchase:{id}), so a
        // race with a late real confirmation cannot double-credit.
        var stuckPurchases = await db.CreditPurchaseSagas
            .Where(s => (s.Status == "Pending" || s.Status == "Abandoned") && s.StartedAt < staleThreshold)
            .OrderBy(s => s.StartedAt)
            .Take(StuckPurchaseCap)
            .ToListAsync(ct);

        var stuckPurchaseCapReached = stuckPurchases.Count == StuckPurchaseCap;
        if (stuckPurchaseCapReached)
        {
            logger.LogWarning(
                "Stripe reconcile: stuck-purchase cap ({Cap}) reached — there may be more unresolved purchases",
                StuckPurchaseCap);
        }

        var regranted = 0;
        foreach (var saga in stuckPurchases)
        {
            // Per-item isolation: one saga's Stripe lookup failure must not abort the whole re-grant pass.
            try
            {
                var paymentStatus = await stripeGateway.GetCheckoutSessionPaymentStatusAsync(saga.CheckoutSessionId, ct);
                if (paymentStatus is not ("paid" or "no_payment_required"))
                {
                    continue; // Not (yet) paid — leave it; a never-paid purchase stays Abandoned.
                }

                logger.LogWarning(
                    "Stripe reconcile: re-granting PAID purchase {PurchaseId} (user {UserId}) whose confirmation was lost",
                    saga.Id, saga.UserId);

                await outbox.PublishAsync(new CreditPurchaseConfirmed(
                    saga.Id, saga.UserId, saga.CreditAmount, saga.BucketExpiryDays, StripeEventId: $"reconcile:{saga.Id}"));
                regranted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Stripe reconcile: skipping stuck purchase {PurchaseId} after an error; will retry next run", saga.Id);
            }
        }

        if (regranted > 0)
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }

        return new ReconcileStripeResponse(
            stuckEvents.Count,
            driftCount,
            regranted,
            stuckEventCapReached,
            subscriptionCapReached,
            stuckPurchaseCapReached);
    }
}
