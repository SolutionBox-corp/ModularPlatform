namespace ModularPlatform.Billing.Security;

/// <summary>
/// Stripe configuration bound from <c>Billing:Stripe</c>. <see cref="WebhookSecret"/> is the signing secret
/// used to verify the webhook signature against the RAW request body (never trust an unsigned payload).
/// </summary>
internal sealed class StripeOptions
{
    public const string SectionName = "Billing:Stripe";

    public string WebhookSecret { get; set; } = string.Empty;
}
