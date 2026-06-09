using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Gdpr.Entities;

/// <summary>
/// Append-only record of a single consent state transition for a subject. We never UPDATE a
/// consent row — granting or withdrawing inserts a new row, so the full consent history is
/// auditable. The current state for a (UserId, ConsentType) is the most recently recorded row.
/// Tenant-scoped; audit + xmin concurrency applied by convention.
/// </summary>
internal sealed class ConsentRecord : Entity, IUserOwned
{
    public Guid UserId { get; set; }
    public string ConsentType { get; set; } = string.Empty;
    public bool Granted { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

internal sealed class ConsentRecordConfiguration : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("consent_records");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ConsentType).HasMaxLength(128).IsRequired();
        builder.HasIndex(c => new { c.UserId, c.ConsentType });
    }
}
