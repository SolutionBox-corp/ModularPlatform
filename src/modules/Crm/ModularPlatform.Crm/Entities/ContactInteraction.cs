using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A logged interaction with a <see cref="Contact"/> ("called him on…", "sent a follow-up", a note). Owned by the
/// user (per-user RLS) and tenant-scoped. References its contact by Id (no navigation, no cross-table JOIN beyond
/// this module). <see cref="Body"/> is a free-text activity log the user reads back; it is [PersonalData] (audit
/// crypto-shred) AND [Encrypted] at rest under the user's DEK, so user erasure renders it unrecoverable in both the
/// live row and the audit trail. It is not a list filter target, so encrypting it costs no query capability.
/// </summary>
internal sealed class ContactInteraction : AuditableEntity, ITenantScoped, IUserOwned, IDataSubject
{
    public Guid UserId { get; set; }
    public Guid ContactId { get; set; }
    public Guid? DealId { get; set; }

    /// <summary>call | email | note | meeting. Validated at the edge; stored as text.</summary>
    public string Type { get; set; } = InteractionTypes.Note;

    public DateTimeOffset OccurredAt { get; set; }

    [PersonalData]
    [Encrypted]
    public string? Body { get; set; }

    Guid IDataSubject.SubjectId => UserId;
}

/// <summary>The allowed <see cref="ContactInteraction.Type"/> values.</summary>
internal static class InteractionTypes
{
    public const string Call = "call";
    public const string Email = "email";
    public const string Note = "note";
    public const string Meeting = "meeting";

    public static readonly string[] All = [Call, Email, Note, Meeting];
    public static bool IsValid(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
}

internal sealed class ContactInteractionConfiguration : IEntityTypeConfiguration<ContactInteraction>
{
    public void Configure(EntityTypeBuilder<ContactInteraction> builder)
    {
        builder.ToTable("crm_contact_interactions");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Type).HasMaxLength(32).IsRequired();
        // Encrypted at rest: the column stores a penc:v2 envelope (longer than the plaintext), so it is unbounded text
        // rather than varchar(8192). The plaintext length is bounded by the validator (8192).
        builder.Property(i => i.Body);
        builder.HasIndex(i => new { i.ContactId, i.OccurredAt });
        builder.HasIndex(i => new { i.DealId, i.OccurredAt });
        builder.HasIndex(i => i.UserId);
    }
}
