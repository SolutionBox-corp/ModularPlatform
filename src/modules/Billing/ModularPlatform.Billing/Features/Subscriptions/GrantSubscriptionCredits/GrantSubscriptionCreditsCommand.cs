using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Security;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.GrantSubscriptionCredits;

/// <summary>
/// Per-period credit grant for a paid subscription invoice (<c>invoice.paid</c>). Exactly once per invoice:
/// the ledger idempotency key is <c>sub-invoice:{invoiceId}</c>, so webhook redelivery, the reconciliation
/// job, or an out-of-order replay can never double-grant. When the local mirror is missing (invoice arrived
/// before the subscription event), it is upserted from Stripe object state first.
/// </summary>
internal sealed record GrantSubscriptionCreditsCommand(string StripeSubscriptionId, string InvoiceId) : ICommand;

internal sealed class GrantSubscriptionCreditsHandler(
    BillingDbContext db,
    IDispatcher dispatcher,
    IOptions<SubscriptionOptions> options)
    : ICommandHandler<GrantSubscriptionCreditsCommand, Unit>
{
    public async Task<Unit> Handle(GrantSubscriptionCreditsCommand command, CancellationToken ct)
    {
        var subscription = await db.Subscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);

        if (subscription is null)
        {
            // Out-of-order: the invoice beat the subscription webhook. Mirror from object state, then retry.
            await dispatcher.Send(new UpsertSubscriptionFromStripeCommand(command.StripeSubscriptionId), ct);
            subscription = await db.Subscriptions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.StripeSubscriptionId == command.StripeSubscriptionId, ct);
            if (subscription is null)
            {
                return Unit.Value; // Not ours (no platform metadata in Stripe) — nothing to grant.
            }
        }

        var plan = options.Value.Plans.FirstOrDefault(p => p.PlanKey == subscription.PlanKey);
        if (plan is null || plan.CreditsPerPeriod <= 0)
        {
            return Unit.Value; // Plan without a credit grant — subscription is access-only.
        }

        await dispatcher.Send(new CreditTopUpCommand(
            subscription.UserId,
            plan.CreditsPerPeriod,
            plan.BucketExpiryDays,
            IdempotencyKey: $"sub-invoice:{command.InvoiceId}"), ct);

        return Unit.Value;
    }
}
