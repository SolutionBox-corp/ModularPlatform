using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

internal enum HoldStatus
{
    Active,
    Confirmed,
    Released,
    Expired,
}

/// <summary>
/// An active reservation against an account. <c>available = posted - sum(active, non-expired holds)</c>.
/// The reservation's <see cref="Id"/> is the public reservationId. A hold has a hard <see cref="ExpiresAt"/>
/// so an expired reservation is ignored by the availability query even before the sweep job runs.
/// </summary>
internal sealed class CreditHold : Entity
{
    public Guid AccountId { get; set; }
    public long Amount { get; set; }
    public HoldStatus Status { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

internal sealed class CreditHoldConfiguration : IEntityTypeConfiguration<CreditHold>
{
    public void Configure(EntityTypeBuilder<CreditHold> builder)
    {
        builder.ToTable("credit_holds");
        builder.HasKey(h => h.Id);
        builder.Property(h => h.AccountId).IsRequired();
        builder.Property(h => h.Amount).IsRequired();
        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(h => h.ExpiresAt).IsRequired();
        builder.Property(h => h.CreatedAt).IsRequired();
        builder.Property(h => h.ResolvedAt);
        builder.HasIndex(h => h.AccountId);
        builder.HasIndex(h => new { h.AccountId, h.Status });
    }
}
