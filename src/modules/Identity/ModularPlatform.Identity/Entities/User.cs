using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// A platform user. Flat aggregate — no navigation to RefreshTokens; they reference UserId.
/// Tenant-scoped + soft-deletable. Audit + xmin concurrency are applied by convention.
/// </summary>
internal sealed class User : AuditableEntity, ITenantScoped, ISoftDeletable
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Locale { get; set; } = "en";
    public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(128);
        builder.Property(u => u.Locale).HasMaxLength(8).IsRequired();
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}
