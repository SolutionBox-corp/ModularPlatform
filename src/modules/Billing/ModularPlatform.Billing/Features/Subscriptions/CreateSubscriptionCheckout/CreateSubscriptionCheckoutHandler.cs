using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Security;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.CreateSubscriptionCheckout;

/// <summary>
/// Starts a Stripe Checkout for a configured plan. The local mirror row is NOT pre-created — it materializes
/// from the <c>customer.subscription.created</c> webhook (Stripe = source of truth, no orphaned Pending rows
/// for abandoned checkouts). Metadata (<c>user_id</c>, <c>plan_key</c>) rides on BOTH the session and the
/// subscription object so every later webhook/reconcile read is self-describing.
/// </summary>
internal sealed class CreateSubscriptionCheckoutHandler(
    BillingDbContext db,
    IStripeGateway gateway,
    ITenantContext tenant,
    IOptions<SubscriptionOptions> subscriptionOptions,
    IOptions<StripeOptions> stripeOptions)
    : ICommandHandler<CreateSubscriptionCheckoutCommand, CreateSubscriptionCheckoutResponse>
{
    public async Task<CreateSubscriptionCheckoutResponse> Handle(
        CreateSubscriptionCheckoutCommand command, CancellationToken ct)
    {
        var plan = subscriptionOptions.Value.Plans.FirstOrDefault(p => p.PlanKey == command.PlanKey)
            ?? throw new NotFoundException("billing.subscription.plan_not_found", "Unknown subscription plan.");

        var hasLiveSubscription = await db.Subscriptions.AnyAsync(
            s => s.UserId == command.UserId && s.Status != SubscriptionStatus.Canceled, ct);
        if (hasLiveSubscription)
        {
            throw new ConflictException(
                "billing.subscription.already_active", "The user already has an active subscription.");
        }

        var stripe = stripeOptions.Value;
        var metadata = new Dictionary<string, string>
        {
            ["user_id"] = command.UserId.ToString(),
            ["plan_key"] = plan.PlanKey,
        };
        // Stamp the caller's tenant so the SYSTEM Worker can resolve it from the session metadata at grant time.
        if (tenant.TenantId is { } tenantId)
        {
            metadata["tenant_id"] = tenantId.ToString();
        }

        var session = await gateway.CreateCheckoutSessionAsync(new CheckoutSessionSpec(
            Mode: "subscription",
            PriceId: plan.StripePriceId,
            ClientReferenceId: command.UserId.ToString(),
            Metadata: metadata,
            AutomaticTax: stripe.AutomaticTax,
            AllowPromotionCodes: stripe.AllowPromotionCodes,
            SuccessUrl: stripe.SuccessUrl,
            CancelUrl: stripe.CancelUrl), ct);

        return new CreateSubscriptionCheckoutResponse(session.SessionId, session.Url);
    }
}
