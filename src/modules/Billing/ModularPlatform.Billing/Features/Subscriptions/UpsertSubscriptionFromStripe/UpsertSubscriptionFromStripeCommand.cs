using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;

/// <summary>
/// Reconciles the local subscription mirror from Stripe OBJECT state — the same command serves every
/// <c>customer.subscription.*</c> webhook AND the reconciliation job, so out-of-order/duplicate deliveries
/// all converge on whatever Stripe currently says (the source of truth). Idempotent: UNIQUE
/// <c>subscriptions.StripeSubscriptionId</c> + catch <c>DbUpdateException</c> resolves creation races.
/// Publishes Activated/Canceled integration events only on actual transitions.
/// </summary>
internal sealed record UpsertSubscriptionFromStripeCommand(string StripeSubscriptionId) : ICommand;

internal sealed class UpsertSubscriptionFromStripeHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IStripeGateway gateway,
    IRealtimePublisher realtime,
    IClock clock)
    : ICommandHandler<UpsertSubscriptionFromStripeCommand, Unit>
{
    public async Task<Unit> Handle(UpsertSubscriptionFromStripeCommand command, CancellationToken ct)
    {
        var state = await gateway.GetSubscriptionAsync(command.StripeSubscriptionId, ct);
        if (state is null)
        {
            return Unit.Value; // Unknown in Stripe — nothing authoritative to mirror.
        }

        if (!state.Metadata.TryGetValue("user_id", out var rawUserId) || !Guid.TryParse(rawUserId, out var userId))
        {
            return Unit.Value; // Not provisioned through this platform (no owner metadata) — not ours.
        }

        state.Metadata.TryGetValue("plan_key", out var planKey);

        var db = outbox.DbContext;
        var subscription = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == state.SubscriptionId, ct);

        var previousStatus = subscription?.Status;

        if (subscription is null)
        {
            subscription = new Subscription { StripeSubscriptionId = state.SubscriptionId, UserId = userId };
            db.Subscriptions.Add(subscription);
        }

        subscription.PlanKey = planKey ?? subscription.PlanKey;
        subscription.StripeCustomerId = state.CustomerId;
        subscription.Status = MapStatus(state.Status);
        subscription.CurrentPeriodEnd = state.CurrentPeriodEnd;
        subscription.CancelAtPeriodEnd = state.CancelAtPeriodEnd;

        var activated = previousStatus != SubscriptionStatus.Active && subscription.Status == SubscriptionStatus.Active;
        var canceled = previousStatus is not null and not SubscriptionStatus.Canceled
                       && subscription.Status == SubscriptionStatus.Canceled;

        if (activated)
        {
            await outbox.PublishAsync(new SubscriptionActivatedIntegrationEvent(
                Guid.CreateVersion7(), clock.UtcNow, userId, subscription.PlanKey, subscription.CurrentPeriodEnd));
        }
        else if (canceled)
        {
            await outbox.PublishAsync(new SubscriptionCanceledIntegrationEvent(
                Guid.CreateVersion7(), clock.UtcNow, userId, subscription.PlanKey));
        }

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // Lost the UNIQUE(StripeSubscriptionId) creation race — the winner mirrored the same Stripe
            // state (both read the object, not the event), so converging is a no-op. The winner already
            // fired any realtime push for the transition; our rolled-back write must emit nothing.
            return Unit.Value;
        }

        // Post-commit realtime nudge so the FE refreshes subscription state live (no polling). Non-transactional,
        // so it MUST fire AFTER the commit — only an actual activation/cancellation transition that committed
        // emits an event; the FE uses the event TYPE to invalidate its subscription query.
        if (activated || canceled)
        {
            await realtime.PublishToUserAsync(
                userId, "billing.subscription_changed", new { status = subscription.Status.ToString() }, ct);
        }

        return Unit.Value;
    }

    /// <summary>Shared Stripe→local status mapping (the reconcile sweep compares with the same rules).</summary>
    internal static SubscriptionStatus MapStatus(string stripeStatus) => stripeStatus switch
    {
        "active" or "trialing" => SubscriptionStatus.Active,
        "past_due" or "unpaid" or "paused" => SubscriptionStatus.PastDue,
        "canceled" or "incomplete_expired" => SubscriptionStatus.Canceled,
        _ => SubscriptionStatus.Pending, // "incomplete" and anything new
    };
}
