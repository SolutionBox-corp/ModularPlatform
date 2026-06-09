using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// A named role (e.g. <c>admin</c>) that bundles permissions. Global authz config — NOT tenant/user scoped, so
/// it is readable by the seeder and writable by admins across users. <see cref="IsSystem"/> roles are seeded and
/// must not be deleted.
/// </summary>
internal sealed class Role : Entity
{
    public string Name { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(64).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
    }
}
