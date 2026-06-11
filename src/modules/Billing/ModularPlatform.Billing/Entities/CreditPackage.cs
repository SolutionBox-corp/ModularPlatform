using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// A purchasable credit package, each mapped to a one-time Stripe Price. Discount math stays in Stripe;
/// this row only records the catalogue entry (credit amount, list price, expiry policy, active flag).
/// </summary>
internal sealed class CreditPackage : AuditableEntity
{
    /// <summary>
    /// The tenant whose catalogue this package belongs to (B2B: a tenant offers its OWN packages, bought by its OWN
    /// members). Null = a platform-global package available to every tenant. Explicit (not <c>ITenantScoped</c>) so the
    /// list/purchase filter can include BOTH the caller's tenant AND global packages (the EF filter would hide null).
    /// </summary>
    public Guid? TenantId { get; set; }

    public string Name { get; set; } = string.Empty;
    public long CreditAmount { get; set; }
    public decimal Price { get; set; }
    public int? BucketExpiryDays { get; set; }
    public bool Active { get; set; }
    public string? StripePriceId { get; set; }
}

internal sealed class CreditPackageConfiguration : IEntityTypeConfiguration<CreditPackage>
{
    public void Configure(EntityTypeBuilder<CreditPackage> builder)
    {
        builder.ToTable("credit_packages");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.Property(p => p.CreditAmount).IsRequired();
        builder.Property(p => p.Price).HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.BucketExpiryDays);
        builder.Property(p => p.Active).IsRequired();
        builder.Property(p => p.StripePriceId).HasMaxLength(256);
        builder.HasIndex(p => p.StripePriceId);
        builder.HasIndex(p => p.TenantId);
    }
}
