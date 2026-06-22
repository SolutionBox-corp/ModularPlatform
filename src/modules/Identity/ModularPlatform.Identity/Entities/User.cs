using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Identity.Entities;

/// <summary>
/// A platform user. Flat aggregate — no navigation to RefreshTokens; they reference UserId.
/// Tenant-scoped + soft-deletable. Audit + xmin concurrency are applied by convention.
/// The user IS its own data subject: PII is crypto-shredded under its own DEK both in the audit trail
/// ([PersonalData]) and AT REST in the live columns ([Encrypted] — sealed on save, decrypted on read).
/// Lookups by e-mail go through <see cref="EmailHash"/>, the keyed blind index over the normalized address
/// (an HMAC is not reversible, so the hash column itself is not personal data).
/// </summary>
internal sealed class User : AuditableEntity, ITenantScoped, ISoftDeletable, IDataSubject
{
    [PersonalData]
    [Encrypted]
    public string Email { get; set; } = string.Empty;

    /// <summary>Blind index: HMAC of <c>Email.Trim().ToUpperInvariant()</c>; carries the UNIQUE constraint.</summary>
    public string EmailHash { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    [PersonalData]
    [Encrypted]
    public string? DisplayName { get; set; }
    public string Locale { get; set; } = "en";

    Guid IDataSubject.SubjectId => Id;

    /// <summary>Consecutive failed login attempts since the last success; resets on success or lockout.</summary>
    public int FailedAccessCount { get; set; }

    /// <summary>When set and in the future, login is rejected even with correct credentials (account lockout).</summary>
    public DateTimeOffset? LockoutEndUtc { get; set; }

    /// <summary>Policy identifier of the terms version the user accepted at registration (GDPR Art. 7(1) provable consent). Not PII.</summary>
    public string? AcceptedTermsVersion { get; set; }

    /// <summary>When the user accepted the terms; null when no acceptance was recorded.</summary>
    public DateTimeOffset? AcceptedTermsAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        // Encrypted at rest: the column stores a penc:v2 envelope, not the address — size accordingly.
        builder.Property(u => u.Email).HasMaxLength(1024).IsRequired();
        builder.Property(u => u.EmailHash).HasMaxLength(64).IsRequired().HasDefaultValue(string.Empty);
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(1024);
        builder.Property(u => u.Locale).HasMaxLength(8).IsRequired();
        builder.Property(u => u.FailedAccessCount).IsRequired().HasDefaultValue(0);
        builder.Property(u => u.LockoutEndUtc);
        builder.Property(u => u.AcceptedTermsVersion).HasMaxLength(32);
        // Filtered: pre-backfill legacy rows hold '' until PiiEncryptionBackfill stamps their hash.
        builder.HasIndex(u => u.EmailHash).IsUnique().HasFilter("\"EmailHash\" <> ''");
    }
}
