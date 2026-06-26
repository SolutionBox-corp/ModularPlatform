using Stripe;

namespace ModularPlatform.Billing.Stripe;

/// <summary>
/// The ONE seam to the Stripe API (anti-corruption layer). Every outbound Stripe call in this module goes
/// through this port: handlers stay testable (the in-memory <see cref="FakeStripeGateway"/> replaces it under
/// <c>Billing:Stripe:UseFakeGateway</c>) and Stripe SDK quirks stay in <see cref="StripeGateway"/>. Results are
/// our own records — except <see cref="Event"/>, which the webhook layer already speaks natively.
/// </summary>
internal interface IStripeGateway
{
    /// <summary>Refetches the event from Stripe — reconcile against CURRENT object state, never trust webhook payload order.</summary>
    Task<Event> GetEventAsync(string eventId, CancellationToken ct);

    Task<CheckoutSessionRef> CreateCheckoutSessionAsync(CheckoutSessionSpec spec, CancellationToken ct);

    /// <summary>Current subscription state straight from Stripe (the source of truth), or null when it does not exist.</summary>
    Task<StripeSubscriptionState?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct);

    Task CancelSubscriptionAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct);

    /// <summary>
    /// Creates a Stripe Customer Portal session for <paramref name="customerId"/> and returns its hosted URL. The
    /// portal is where a customer manages payment methods and views/downloads past invoices &amp; receipts — Stripe
    /// hosts it, so the platform never touches card data. Returns the URL to redirect the browser to.
    /// </summary>
    Task<string> CreateBillingPortalSessionAsync(string customerId, string returnUrl, CancellationToken ct);

    /// <summary>Active promotion code lookup by customer-facing code, or null when unknown/inactive.</summary>
    Task<PromotionCodeState?> FindActivePromotionCodeAsync(string code, CancellationToken ct);

    /// <summary>
    /// The live <c>payment_status</c> of a Checkout session ("paid" | "unpaid" | "no_payment_required"), or null
    /// when the session is unknown. Used by the reconcile sweep to safely re-grant a purchase whose confirmation
    /// dead-lettered — only when Stripe confirms the funds were actually captured.
    /// </summary>
    Task<string?> GetCheckoutSessionPaymentStatusAsync(string sessionId, CancellationToken ct);
}

/// <summary>What a checkout session needs — one shape for one-time package payments and subscriptions.</summary>
internal sealed record CheckoutSessionSpec(
    string Mode, // "payment" | "subscription"
    string PriceId,
    string ClientReferenceId,
    IReadOnlyDictionary<string, string> Metadata,
    bool AutomaticTax,
    bool AllowPromotionCodes,
    string SuccessUrl,
    string CancelUrl);

internal sealed record CheckoutSessionRef(string SessionId, string Url);

/// <summary>Stripe subscription state distilled to what the module persists/reconciles.</summary>
internal sealed record StripeSubscriptionState(
    string SubscriptionId,
    string Status, // raw Stripe status: incomplete|incomplete_expired|trialing|active|past_due|canceled|unpaid|paused
    string? CustomerId,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    IReadOnlyDictionary<string, string> Metadata);

internal sealed record PromotionCodeState(
    string Code,
    decimal? PercentOff,
    long? AmountOff,
    string? Currency);
