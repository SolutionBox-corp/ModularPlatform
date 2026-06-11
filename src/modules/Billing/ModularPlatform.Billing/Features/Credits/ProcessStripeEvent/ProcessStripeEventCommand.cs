using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Billing.Features.Subscriptions.GrantSubscriptionCredits;
using ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Sagas;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using Stripe;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Credits.ProcessStripeEvent;

/// <summary>
/// IDEMPOTENT Stripe event ROUTER (runs in the Worker, inbox-deduped). Refetches the event through
/// <see cref="IStripeGateway"/> (reconcile against CURRENT state, never webhook payload order) and routes:
/// <list type="bullet">
/// <item><c>checkout.session.completed</c> with <c>purchase_type=package</c> → <see cref="CreditPurchaseConfirmed"/>
/// for the purchase saga (published via the outbox, atomically with the ProcessedAt stamp).</item>
/// <item><c>customer.subscription.*</c> → upsert the local mirror from Stripe OBJECT state (out-of-order safe).</item>
/// <item><c>invoice.paid</c> → per-period credit grant (idempotency key <c>sub-invoice:{invoiceId}</c>).</item>
/// <item>any event carrying <c>user_id</c>/<c>credit_amount</c> metadata → direct idempotent top-up
/// (idempotency key = Stripe event id).</item>
/// </list>
/// </summary>
internal sealed record ProcessStripeEventCommand(string StripeEventId) : ICommand;

internal sealed class ProcessStripeEventHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IStripeGateway gateway,
    IDispatcher dispatcher,
    IClock clock)
    : ICommandHandler<ProcessStripeEventCommand, Unit>
{
    public async Task<Unit> Handle(ProcessStripeEventCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        var record = await db.StripeEvents.FirstOrDefaultAsync(e => e.StripeEventId == command.StripeEventId, ct);
        if (record is null || record.ProcessedAt is not null)
        {
            return Unit.Value;
        }

        var stripeEvent = await gateway.GetEventAsync(command.StripeEventId, ct);

        // Provider is fixed for this path; backfill the tenant from the LIVE object metadata if the ingest payload
        // didn't carry it (the row's tenant is a routing hint only — it never gates the grant below).
        record.Provider = "stripe";
        if (record.TenantId is null && TryExtractTenantId(stripeEvent, out var resolvedTenantId))
        {
            record.TenantId = resolvedTenantId;
        }

        switch (stripeEvent.Type)
        {
            case "checkout.session.completed" or "checkout.session.async_payment_succeeded"
                when stripeEvent.Data?.Object is global::Stripe.Checkout.Session session
                     && session.Metadata is not null
                     && session.Metadata.TryGetValue("purchase_type", out var purchaseType)
                     && purchaseType == "package":
                // Grant ONLY once funds are actually captured. checkout.session.completed fires for delayed
                // payment methods (SEPA, bank transfer) while PaymentStatus is "unpaid"; the authoritative paid
                // signal then arrives as async_payment_succeeded. Never grant on an unpaid/abandoned session —
                // the saga timeout abandons it (a late paid signal still grants via the saga's NotFound path).
                if (IsPaid(session))
                {
                    await PublishPurchaseConfirmed(session, command.StripeEventId, ct);
                }

                break;

            // Any other checkout-session event (unpaid "completed", async_payment_failed, expired, non-package)
            // is NOT a credit grant — never let its user_id/credit_amount metadata reach the generic top-up below.
            case var checkoutType when checkoutType.StartsWith("checkout.session.", StringComparison.Ordinal):
                break;

            case "customer.subscription.created" or "customer.subscription.updated" or "customer.subscription.deleted"
                when stripeEvent.Data?.Object is Subscription subscription:
                await dispatcher.Send(new UpsertSubscriptionFromStripeCommand(subscription.Id), ct);
                break;

            case "invoice.paid"
                when stripeEvent.Data?.Object is Invoice invoice
                     && invoice.Parent?.SubscriptionDetails?.SubscriptionId is { Length: > 0 } subscriptionId:
                await dispatcher.Send(new GrantSubscriptionCreditsCommand(subscriptionId, invoice.Id), ct);
                break;

            default:
                if (TryExtractTopUp(stripeEvent, out var userId, out var amount, out var bucketExpiryDays))
                {
                    await dispatcher.Send(
                        new CreditTopUpCommand(userId, amount, bucketExpiryDays, command.StripeEventId), ct);
                }

                break;
        }

        record.ProcessedAt = clock.UtcNow;
        // Commits the ProcessedAt stamp AND any queued saga message in ONE transaction.
        await outbox.SaveChangesAndFlushMessagesAsync();
        return Unit.Value;
    }

    // Stripe payment_status: "paid" | "unpaid" | "no_payment_required" (zero-amount / fully-discounted).
    private static bool IsPaid(global::Stripe.Checkout.Session session) =>
        session.PaymentStatus is "paid" or "no_payment_required";

    private async Task PublishPurchaseConfirmed(
        global::Stripe.Checkout.Session session, string stripeEventId, CancellationToken ct)
    {
        var metadata = session.Metadata;
        if (!metadata.TryGetValue("purchase_id", out var rawPurchaseId)
            || !Guid.TryParse(rawPurchaseId, out var purchaseId)
            || !metadata.TryGetValue("user_id", out var rawUserId)
            || !Guid.TryParse(rawUserId, out var userId)
            || !metadata.TryGetValue("credit_amount", out var rawAmount)
            || !long.TryParse(rawAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0)
        {
            return; // Malformed metadata — not ours to grant; the reconciliation job surfaces unprocessed drift.
        }

        int? expiryDays = null;
        if (metadata.TryGetValue("bucket_expiry_days", out var rawExpiry)
            && int.TryParse(rawExpiry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > 0)
        {
            expiryDays = parsed;
        }

        await outbox.PublishAsync(new CreditPurchaseConfirmed(purchaseId, userId, amount, expiryDays, stripeEventId));
    }

    private static bool TryExtractTenantId(Event stripeEvent, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        var metadata = (stripeEvent.Data?.Object as IHasMetadata)?.Metadata;
        return metadata is not null
            && metadata.TryGetValue("tenant_id", out var rawTenantId)
            && Guid.TryParse(rawTenantId, out tenantId);
    }

    private static bool TryExtractTopUp(Event stripeEvent, out Guid userId, out long amount, out int? bucketExpiryDays)
    {
        userId = Guid.Empty;
        amount = 0;
        bucketExpiryDays = null;

        var metadata = (stripeEvent.Data?.Object as IHasMetadata)?.Metadata;
        if (metadata is null
            || !metadata.TryGetValue("user_id", out var rawUserId)
            || !Guid.TryParse(rawUserId, out userId)
            || !metadata.TryGetValue("credit_amount", out var rawAmount)
            || !long.TryParse(rawAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount)
            || amount <= 0)
        {
            return false;
        }

        if (metadata.TryGetValue("bucket_expiry_days", out var rawExpiry)
            && int.TryParse(rawExpiry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiry)
            && expiry > 0)
        {
            bucketExpiryDays = expiry;
        }

        return true;
    }
}
