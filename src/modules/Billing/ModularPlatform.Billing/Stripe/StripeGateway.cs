using Microsoft.Extensions.Options;
using ModularPlatform.Billing.Security;
using ModularPlatform.Cqrs;
using Stripe;
using Stripe.Checkout;

namespace ModularPlatform.Billing.Stripe;

/// <summary>
/// Real Stripe adapter over <c>Stripe.net</c>. One lazily-built <see cref="StripeClient"/> per process,
/// authenticated by <c>Billing:Stripe:ApiKey</c>; any call without a configured key fails fast with
/// <c>billing.stripe.not_configured</c> (a deployment without Stripe keeps the rest of Billing working).
/// Stripe model quirks (period end living on subscription ITEMS, promotion → coupon nesting) are absorbed here.
/// </summary>
internal sealed class StripeGateway(IOptions<StripeOptions> options) : IStripeGateway
{
    private readonly Lazy<StripeClient> _client = new(() =>
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new BusinessRuleException(
                "billing.stripe.not_configured",
                "Stripe is not configured (Billing:Stripe:ApiKey is missing).");
        }

        return new StripeClient(apiKey);
    });

    public Task<Event> GetEventAsync(string eventId, CancellationToken ct) =>
        new EventService(_client.Value).GetAsync(eventId, cancellationToken: ct);

    public async Task<CheckoutSessionRef> CreateCheckoutSessionAsync(CheckoutSessionSpec spec, CancellationToken ct)
    {
        var createOptions = new SessionCreateOptions
        {
            Mode = spec.Mode,
            LineItems = [new SessionLineItemOptions { Price = spec.PriceId, Quantity = 1 }],
            ClientReferenceId = spec.ClientReferenceId,
            Metadata = spec.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
            AllowPromotionCodes = spec.AllowPromotionCodes ? true : null,
            AutomaticTax = spec.AutomaticTax ? new SessionAutomaticTaxOptions { Enabled = true } : null,
            SuccessUrl = spec.SuccessUrl,
            CancelUrl = spec.CancelUrl,
        };

        if (spec.Mode == "subscription")
        {
            // Mirror the metadata onto the subscription object itself so customer.subscription.* events
            // (and reconcile reads) carry user_id/plan_key without an extra session lookup.
            createOptions.SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = spec.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
            };
        }

        var session = await new SessionService(_client.Value).CreateAsync(createOptions, cancellationToken: ct);
        return new CheckoutSessionRef(session.Id, session.Url);
    }

    public async Task<StripeSubscriptionState?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct)
    {
        Subscription subscription;
        try
        {
            subscription = await new SubscriptionService(_client.Value)
                .GetAsync(subscriptionId, cancellationToken: ct);
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        return ToState(subscription);
    }

    public async Task<string?> GetCheckoutSessionPaymentStatusAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var session = await new SessionService(_client.Value).GetAsync(sessionId, cancellationToken: ct);
            return session.PaymentStatus;
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task CancelSubscriptionAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct)
    {
        var service = new SubscriptionService(_client.Value);
        if (atPeriodEnd)
        {
            await service.UpdateAsync(
                subscriptionId,
                new SubscriptionUpdateOptions { CancelAtPeriodEnd = true },
                cancellationToken: ct);
        }
        else
        {
            await service.CancelAsync(subscriptionId, cancellationToken: ct);
        }
    }

    public async Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl, CancellationToken ct)
    {
        var session = await new global::Stripe.BillingPortal.SessionService(_client.Value).CreateAsync(
            new global::Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = customerId,
                ReturnUrl = returnUrl,
            },
            cancellationToken: ct);
        return session.Url;
    }

    public async Task<PromotionCodeState?> FindActivePromotionCodeAsync(string code, CancellationToken ct)
    {
        var list = await new PromotionCodeService(_client.Value).ListAsync(
            new PromotionCodeListOptions { Code = code, Active = true, Limit = 1 },
            cancellationToken: ct);

        var match = list.Data.FirstOrDefault();
        if (match is null)
        {
            return null;
        }

        var coupon = match.Promotion?.Coupon;
        return new PromotionCodeState(match.Code, coupon?.PercentOff, coupon?.AmountOff, coupon?.Currency);
    }

    private static StripeSubscriptionState ToState(Subscription subscription)
    {
        // Stripe moved the billing period onto subscription ITEMS — the subscription-level period is the
        // latest item period end (single-item subscriptions in practice).
        DateTimeOffset? periodEnd = subscription.Items?.Data is { Count: > 0 } items
            ? items.Max(i => i.CurrentPeriodEnd)
            : null;

        return new StripeSubscriptionState(
            subscription.Id,
            subscription.Status,
            subscription.CustomerId,
            periodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.Metadata ?? new Dictionary<string, string>());
    }
}
