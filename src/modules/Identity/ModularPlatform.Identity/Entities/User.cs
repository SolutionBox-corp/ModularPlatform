using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// A platform user. Flat aggregate — no navigation to RefreshTokens; they reference UserId.
/// Tenant-scoped + soft-deletable. Audit + xmin concurrency are applied by convention.
/// The user IS its own data subject, so PII captured in the audit trail is crypto-shredded under its own DEK.
/// </summary>
internal sealed class User : AuditableEntity, ITenantScoped, ISoftDeletable, IDataSubject
{
    [PersonalData]
    public string Email { get; set; } = string.Empty;
    [PersonalData]
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    [PersonalData]
    public string? DisplayName { get; set; }
    public string Locale { get; set; } = "en";

    Guid IDataSubject.SubjectId => Id;

    /// <summary>Consecutive failed login attempts since the last success; resets on success or lockout.</summary>
    public int FailedAccessCount { get; set; }

    /// <summary>When set and in the future, login is rejected even with correct credentials (account lockout).</summary>
    public DateTimeOffset? LockoutEndUtc { get; set; }

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
        builder.Property(u => u.FailedAccessCount).IsRequired().HasDefaultValue(0);
        builder.Property(u => u.LockoutEndUtc);
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}
