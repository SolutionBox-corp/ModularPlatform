using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// Audit anchor for a machine-token issuance. Stores only issuance metadata, never the JWT/access-token plaintext.
/// </summary>
internal sealed class MachineTokenIssuance : AuditableEntity
{
    public Guid TargetTenantId { get; set; }
    public Guid MachineSubjectId { get; set; }
    public string? TokenId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class MachineTokenIssuanceConfiguration : IEntityTypeConfiguration<MachineTokenIssuance>
{
    public void Configure(EntityTypeBuilder<MachineTokenIssuance> builder)
    {
        builder.ToTable("machine_token_issuances");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.TokenId).HasMaxLength(64);
        builder.Property(i => i.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(i => i.TokenId).IsUnique().HasFilter("\"TokenId\" IS NOT NULL");
        builder.HasIndex(i => i.TargetTenantId);
        builder.HasIndex(i => i.MachineSubjectId);
        builder.HasIndex(i => i.CreatedAt);
    }
}
