using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// A fine-grained permission (a <c>PlatformPermissions</c> string, e.g. <c>notifications.send</c>). Seeded from
/// the code catalog; the table exists so roles can reference permissions by FK and admins can inspect them.
/// </summary>
internal sealed class Permission : Entity
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(128).IsRequired();
        builder.HasIndex(p => p.Name).IsUnique();
    }
}
