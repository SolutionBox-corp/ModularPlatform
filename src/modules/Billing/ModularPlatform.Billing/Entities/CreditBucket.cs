using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

/// <summary>
/// A top-up tranche with its own expiry. Spend draws soonest-to-expire first (FIFO). <see cref="Remaining"/>
/// is decremented as credits are consumed; an expired bucket's remaining is swept to an Expiry entry.
/// </summary>
internal sealed class CreditBucket : Entity
{
    public Guid AccountId { get; set; }
    public long Amount { get; set; }
    public long Remaining { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class CreditBucketConfiguration : IEntityTypeConfiguration<CreditBucket>
{
    public void Configure(EntityTypeBuilder<CreditBucket> builder)
    {
        builder.ToTable("credit_buckets");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.AccountId).IsRequired();
        builder.Property(b => b.Amount).IsRequired();
        builder.Property(b => b.Remaining).IsRequired();
        builder.Property(b => b.ExpiresAt);
        builder.Property(b => b.CreatedAt).IsRequired();
        builder.HasIndex(b => b.AccountId);
        builder.HasIndex(b => b.ExpiresAt);
    }
}
