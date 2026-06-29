using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A CRM contact owned by a platform user (tenant-scoped + per-user RLS). The OWNING USER is the data subject
/// (<see cref="IDataSubject.SubjectId"/> => <see cref="UserId"/>, mirroring <c>Notification</c>): its name/e-mail/
/// phone are crypto-shredded under the user's DEK both in the audit trail ([PersonalData]) and AT REST ([Encrypted]
/// — sealed on save, decrypted on read), so the existing user-erasure DEK shred renders the third-party PII (in the
/// live row and the audit trail) unrecoverable. Lookups by e-mail go through <see cref="EmailHash"/>, the keyed
/// blind index over the normalized address (an HMAC is not reversible, so the hash is not personal data).
/// Company/Position/Notes are plain text (so the list can filter on them) but [PersonalData] so audit values are
/// shreddable; the eraser scrubs the live row.
/// </summary>
internal sealed class Contact : AuditableEntity, ITenantScoped, IUserOwned, ISoftDeletable, IDataSubject
{
    public Guid UserId { get; set; }

    [PersonalData]
    [Encrypted]
    public string FullName { get; set; } = string.Empty;

    [PersonalData]
    [Encrypted]
    public string? Email { get; set; }

    /// <summary>Blind index: HMAC of <c>Email.Trim().ToUpperInvariant()</c>; non-unique (a user may hold duplicates).</summary>
    public string? EmailHash { get; set; }

    [PersonalData]
    [Encrypted]
    public string? Phone { get; set; }

    [PersonalData]
    public string? Company { get; set; }

    [PersonalData]
    public string? Position { get; set; }

    [PersonalData]
    public string? Notes { get; set; }

    public string[] Tags { get; set; } = [];

    /// <summary>Lifecycle bucket: lead | active | customer | archived. Validated at the edge; stored as text.</summary>
    public string Status { get; set; } = ContactStatuses.Lead;

    Guid IDataSubject.SubjectId => UserId;

    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>The allowed <see cref="Contact.Status"/> values (stable, lowercase — also used as i18n-safe constants).</summary>
internal static class ContactStatuses
{
    public const string Lead = "lead";
    public const string Active = "active";
    public const string Customer = "customer";
    public const string Archived = "archived";

    public static readonly string[] All = [Lead, Active, Customer, Archived];
    public static bool IsValid(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> builder)
    {
        builder.ToTable("crm_contacts");
        builder.HasKey(c => c.Id);
        // Encrypted at rest: columns store a penc:v2 envelope, not the value — size accordingly.
        builder.Property(c => c.FullName).HasMaxLength(2048).IsRequired();
        builder.Property(c => c.Email).HasMaxLength(2048);
        builder.Property(c => c.EmailHash).HasMaxLength(64);
        builder.Property(c => c.Phone).HasMaxLength(2048);
        builder.Property(c => c.Company).HasMaxLength(256);
        builder.Property(c => c.Position).HasMaxLength(256);
        builder.Property(c => c.Notes).HasMaxLength(8192);
        builder.Property(c => c.Tags).HasColumnType("text[]");
        builder.Property(c => c.Status).HasMaxLength(32).IsRequired();

        builder.HasIndex(c => new { c.UserId, c.Status });
        builder.HasIndex(c => new { c.UserId, c.CreatedAt });
        builder.HasIndex(c => c.EmailHash);
    }
}
