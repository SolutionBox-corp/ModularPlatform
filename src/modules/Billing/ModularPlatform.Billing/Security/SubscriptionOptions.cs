namespace ModularPlatform.Billing.Security;

/// <summary>
/// Subscription plan catalogue bound from <c>Billing:Subscriptions</c>. Plans are CONFIG, not DB — the same
/// base ships differently-shaped products per deployment (consistent with <c>Modules:{Name}:Enabled</c>).
/// Each plan maps a stable <see cref="SubscriptionPlan.PlanKey"/> to a recurring Stripe Price and an optional
/// per-period credit grant (granted exactly once per paid invoice).
/// </summary>
internal sealed class SubscriptionOptions
{
    public const string SectionName = "Billing:Subscriptions";

    public List<SubscriptionPlan> Plans { get; set; } = [];

    /// <summary>Cancel at period end (proration-safe, default) vs immediately.</summary>
    public bool CancelAtPeriodEnd { get; set; } = true;
}

internal sealed class SubscriptionPlan
{
    public string PlanKey { get; set; } = string.Empty;
    public string StripePriceId { get; set; } = string.Empty;
    public long CreditsPerPeriod { get; set; }
    public int? BucketExpiryDays { get; set; }
}
