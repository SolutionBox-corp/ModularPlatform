using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ModularPlatform.Persistence.Audit;

/// <summary>
/// One immutable audit row per Create/Update/Delete. For updates it records ONLY the changed
/// columns and their new values (as JSONB) — never the whole row. PII new-values are the already
/// crypto-shredded ciphertext, so erasing a subject leaves no plaintext behind.
/// Table is per-module (<c>{module}_audit_entries</c>) so module DbContexts never collide.
/// </summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // Create | Update | Delete
    public string ChangedColumns { get; set; } = "[]";  // jsonb array of column names
    public string NewValues { get; set; } = "{}";       // jsonb object of changed column -> new value
    public Guid? UserId { get; set; }
    public Guid? TenantId { get; set; }
    public string? IpAddress { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

internal sealed class AuditEntryConfiguration(string tableName) : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable(tableName);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.EntityType).HasMaxLength(256).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(16).IsRequired();
        builder.Property(a => a.ChangedColumns).HasColumnType("jsonb");
        builder.Property(a => a.NewValues).HasColumnType("jsonb");
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.UserId);
    }
}
