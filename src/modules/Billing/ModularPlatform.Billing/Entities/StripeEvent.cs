using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// Webhook idempotency ledger. <see cref="StripeEventId"/> is uniquely indexed; persisting it in the same
/// transaction as ingest guarantees each Stripe event is processed at most once even on retries/replays.
/// </summary>
internal sealed class StripeEvent : Entity
{
    public string StripeEventId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>
    /// The payment provider that originated the event (default <c>"stripe"</c>). Additive seam for the
    /// provider-agnostic Payments work — the table is not renamed (raw-SQL tests still target <c>stripe_events</c>).
    /// </summary>
    public string Provider { get; set; } = "stripe";

    /// <summary>
    /// The tenant the event belongs to, resolved from the checkout/session metadata (<c>tenant_id</c>) when
    /// present, else <c>null</c> (legacy/global path). Lets the SYSTEM Worker scope the later grant per tenant.
    /// </summary>
    public Guid? TenantId { get; set; }
}

internal sealed class StripeEventConfiguration : IEntityTypeConfiguration<StripeEvent>
{
    public void Configure(EntityTypeBuilder<StripeEvent> builder)
    {
        builder.ToTable("stripe_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.StripeEventId).HasMaxLength(256).IsRequired();
        builder.Property(e => e.Type).HasMaxLength(128).IsRequired();
        builder.Property(e => e.ReceivedAt).IsRequired();
        builder.Property(e => e.ProcessedAt);
        builder.Property(e => e.Provider).HasMaxLength(32).IsRequired().HasDefaultValue("stripe");
        builder.Property(e => e.TenantId);
        builder.HasIndex(e => e.StripeEventId).IsUnique();
    }
}
