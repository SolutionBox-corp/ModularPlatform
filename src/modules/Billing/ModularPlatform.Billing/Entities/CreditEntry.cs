using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Billing.Entities;

internal enum CreditDirection
{
    Debit,
    Credit,
}

internal enum CreditEntryType
{
    Topup,
    Spend,
    Reservation,
    Release,
    Expiry,
    Adjustment,
    Refund,
}

/// <summary>
/// Append-only, IMMUTABLE double-entry ledger row. Never UPDATE or DELETE. Each <see cref="TransactionId"/>
/// groups a balanced set. <see cref="IdempotencyKey"/> is uniquely indexed so re-applying the same logical
/// operation (e.g. a replayed Stripe event) credits exactly once.
/// </summary>
internal sealed class CreditEntry : Entity
{
    public Guid AccountId { get; set; }
    public CreditDirection Direction { get; set; }
    public long Amount { get; set; }
    public Guid TransactionId { get; set; }
    public CreditEntryType Type { get; set; }
    public Guid? BucketId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class CreditEntryConfiguration : IEntityTypeConfiguration<CreditEntry>
{
    public void Configure(EntityTypeBuilder<CreditEntry> builder)
    {
        builder.ToTable("credit_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.AccountId).IsRequired();
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.Amount).IsRequired();
        builder.Property(e => e.TransactionId).IsRequired();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(e => e.BucketId);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(256).IsRequired();
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.HasIndex(e => e.IdempotencyKey).IsUnique();
        builder.HasIndex(e => e.AccountId);
        builder.HasIndex(e => e.TransactionId);
    }
}
