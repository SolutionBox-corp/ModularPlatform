using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Marketing.Entities;

/// <summary>
/// Local read-model copy of the tenant registry fields Marketing needs for fast module-owned lists/reconciliation.
/// Tenancy remains the source of truth; this row is a repairable projection fed by events and reconcile commands.
/// </summary>
internal sealed class MarketingTenantSnapshot : Entity
{
    public Guid TenantId { get; set; }
    public string Subdomain { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid SourceEventId { get; set; }
    public DateTimeOffset SourceUpdatedAt { get; set; }
    public int SchemaVersion { get; set; } = 1;
}

internal sealed class MarketingTenantSnapshotConfiguration : IEntityTypeConfiguration<MarketingTenantSnapshot>
{
    public void Configure(EntityTypeBuilder<MarketingTenantSnapshot> builder)
    {
        builder.ToTable("marketing_tenant_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.Subdomain).HasMaxLength(128).IsRequired();
        builder.Property(s => s.Name).HasMaxLength(200).IsRequired();
        builder.Property(s => s.SchemaVersion).HasDefaultValue(1);
        builder.HasIndex(s => s.TenantId).IsUnique();
        builder.HasIndex(s => s.SourceUpdatedAt);
    }
}
