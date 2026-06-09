using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// Assigns a role to a user (many-to-many). Flat — references both sides by Id, no navigation. A user's effective
/// permissions are the union of the permissions of their roles. NOT user-scoped (admins assign across users).
/// </summary>
internal sealed class UserRole : Entity
{
    public Guid UserId { get; set; }
    public Guid RoleId { get; set; }
}

internal sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_roles");
        builder.HasKey(ur => ur.Id);
        builder.HasIndex(ur => new { ur.UserId, ur.RoleId }).IsUnique();
        builder.HasIndex(ur => ur.UserId);
    }
}
