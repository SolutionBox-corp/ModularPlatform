namespace ModularPlatform.Billing.Security;

/// <summary>
/// Stripe configuration bound from <c>Billing:Stripe</c>. <see cref="WebhookSecret"/> is the signing secret
/// used to verify the webhook signature against the RAW request body (never trust an unsigned payload).
/// <see cref="ApiKey"/> authenticates outbound Stripe calls through <c>IStripeGateway</c>; when empty, any
/// gateway call fails with <c>billing.stripe.not_configured</c> (webhook ingest still works — it only verifies).
/// All commerce knobs are config-driven so the same base ships as differently-shaped products.
/// </summary>
internal sealed class StripeOptions
{
    public const string SectionName = "Billing:Stripe";

    public string WebhookSecret { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Stripe Tax (merchant-of-record VAT) on every checkout session. Requires Stripe Tax enabled in the dashboard.</summary>
    public bool AutomaticTax { get; set; }

    /// <summary>Shows the promo-code box on Stripe Checkout (coupons/promotions live entirely in Stripe).</summary>
    public bool AllowPromotionCodes { get; set; } = true;

    /// <summary>Redirect target after a successful checkout. Required to create a checkout session.</summary>
    public string SuccessUrl { get; set; } = string.Empty;

    /// <summary>Redirect target after an abandoned checkout. Required to create a checkout session.</summary>
    public string CancelUrl { get; set; } = string.Empty;

    /// <summary>Minutes before a pending credit purchase saga gives up on the checkout (abandon timeout).</summary>
    public int CheckoutTimeoutMinutes { get; set; } = 120;

    /// <summary>TESTS ONLY — swaps the real Stripe gateway for the in-memory fake. Never set in production.</summary>
    public bool UseFakeGateway { get; set; }
}
