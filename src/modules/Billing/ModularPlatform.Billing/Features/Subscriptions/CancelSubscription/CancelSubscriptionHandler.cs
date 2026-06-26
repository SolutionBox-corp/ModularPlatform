using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Security;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using Stripe;

namespace ModularPlatform.Billing.Features.Subscriptions.CancelSubscription;

/// <summary>
/// Cancels in STRIPE first (at period end by default — proration-safe), then mirrors the intent locally.
/// The authoritative terminal state still arrives via the <c>customer.subscription.updated/deleted</c>
/// webhook → <c>UpsertSubscriptionFromStripe</c>; the eager local update only keeps the UI honest meanwhile.
/// </summary>
internal sealed class CancelSubscriptionHandler(
    BillingDbContext db,
    IStripeGateway gateway,
    IOptions<SubscriptionOptions> options)
    : ICommandHandler<CancelSubscriptionCommand, CancelSubscriptionResponse>
{
    public async Task<CancelSubscriptionResponse> Handle(CancelSubscriptionCommand command, CancellationToken ct)
    {
        var subscription = await db.Subscriptions
            .Where(s => s.UserId == command.UserId && s.Status != SubscriptionStatus.Canceled)
            .OrderByDescending(s => s.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("billing.subscription.not_found", "No active subscription.");

        var atPeriodEnd = options.Value.CancelAtPeriodEnd;
        try
        {
            await gateway.CancelSubscriptionAsync(subscription.StripeSubscriptionId, atPeriodEnd, ct);
        }
        catch (StripeException ex)
        {
            throw new BusinessRuleException(
                "billing.subscription.provider_failed",
                $"The subscription provider rejected the cancellation: {ex.Message}");
        }

        if (atPeriodEnd)
        {
            subscription.CancelAtPeriodEnd = true;
        }
        else
        {
            subscription.Status = SubscriptionStatus.Canceled;
        }

        await db.SaveChangesAsync(ct);

        return new CancelSubscriptionResponse(
            subscription.Id, subscription.Status.ToString(), subscription.CancelAtPeriodEnd);
    }
}
