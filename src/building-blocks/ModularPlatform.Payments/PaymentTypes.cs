namespace ModularPlatform.Payments;

/// <summary>
/// Provider-neutral payment state. Each adapter normalizes its provider's raw status into this set:
/// Stripe (<c>requires_capture</c>→Authorized, <c>succeeded</c>→Paid, …) and GoPay (<c>PAYMENT_METHOD_CHOSEN</c>→Pending,
/// <c>TIMEOUTED</c>→Expired, …) both map here, so business logic never branches on a provider string.
/// </summary>
public enum PaymentState
{
    Created,
    Pending,
    Authorized,
    Paid,
    Canceled,
    Expired,
    Refunded,
    PartiallyRefunded,
    Failed,
}

/// <summary>Whether a checkout is a one-off payment or sets up a recurring charge.</summary>
public enum CheckoutMode
{
    Payment,
    Subscription,
}

/// <summary>
/// What a customer is being asked to pay. Amounts are ALWAYS in minor units (cents/haléře) — both Stripe and GoPay
/// use minor units, so there is no decimal rounding seam. <see cref="ReferenceId"/> is the merchant's own id for this
/// purchase (used for app-side idempotency — neither provider's id is known until after CreateCheckout). Promotions/tax
/// for GoPay are computed platform-side and baked into the amount; Stripe can do them natively (see capabilities).
/// </summary>
public sealed record CheckoutRequest(
    string ReferenceId,
    long AmountMinorUnits,
    string Currency,
    CheckoutMode Mode,
    string Description,
    IReadOnlyDictionary<string, string> Metadata,
    string SuccessUrl,
    string CancelUrl);

/// <summary>The redirect a host opens to collect payment, plus the provider's own payment/session id (stored as a string to absorb Stripe's <c>pi_…</c> and GoPay's numeric ids).</summary>
public sealed record CheckoutResult(string ProviderPaymentId, string RedirectUrl);

/// <summary>The authoritative state of a payment, always obtained by re-fetching from the provider (mandatory for GoPay, recommended for Stripe).</summary>
public sealed record PaymentSnapshot(
    string ProviderPaymentId,
    PaymentState State,
    long? AmountMinorUnits,
    string? Currency,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>Outcome of a refund. <see cref="State"/> is the resulting state of the ORIGINAL payment (Refunded / PartiallyRefunded).</summary>
public sealed record RefundResult(string RefundId, PaymentState State);

/// <summary>
/// What arrived at the webhook endpoint, carrying the inputs both verification strategies need. Stripe verifies the
/// HMAC over <see cref="RawBody"/> with <see cref="SignatureHeader"/>; GoPay has NO signature — the adapter ignores
/// those and re-fetches the authoritative state by the id in <see cref="Query"/>. Either way
/// <c>VerifyNotificationAsync</c> returns the trustworthy state; the host NEVER acts on the raw payload directly.
/// </summary>
public sealed record NotificationContext(
    string RawBody,
    string? SignatureHeader,
    IReadOnlyDictionary<string, string> Query);

/// <summary>
/// What a provider can do natively, so the host can branch UX/logic instead of pretending parity. GoPay has no
/// signed webhook, no first-class subscription object, no native coupons/tax; Stripe has all of these.
/// </summary>
public sealed record GatewayCapabilities(
    bool SignedWebhooks,
    bool NativeSubscriptions,
    bool NativeCoupons,
    bool NativeTax,
    bool PreAuthorization);
