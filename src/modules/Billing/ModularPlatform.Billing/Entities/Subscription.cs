using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// Local mirror of a Stripe subscription. Rows are written ONLY from Stripe object state (webhook upsert +
/// the reconciliation job) — Stripe is the source of truth; checkout never pre-creates a row, so out-of-order
/// webhook deliveries converge instead of conflicting. The plan itself (price, credits per period) lives in
/// config (<c>Billing:Subscriptions:Plans</c>), referenced by <see cref="PlanKey"/>.
/// </summary>
internal sealed class Subscription : AuditableEntity, IUserOwned
{
    public Guid UserId { get; set; }
    public string PlanKey { get; set; } = string.Empty;
    public string StripeSubscriptionId { get; set; } = string.Empty;
    public string? StripeCustomerId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Pending;
    public DateTimeOffset? CurrentPeriodEnd { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
}

internal enum SubscriptionStatus
{
    Pending,
    Active,
    PastDue,
    Canceled,
}

internal sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.PlanKey).HasMaxLength(64).IsRequired();
        builder.Property(s => s.StripeSubscriptionId).HasMaxLength(256).IsRequired();
        builder.Property(s => s.StripeCustomerId).HasMaxLength(256);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.HasIndex(s => s.StripeSubscriptionId).IsUnique();
        builder.HasIndex(s => s.UserId);
    }
}
