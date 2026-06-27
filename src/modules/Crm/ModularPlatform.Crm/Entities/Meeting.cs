using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A scheduled (or held) meeting owned by a user (per-user RLS), tenant-scoped, soft-deletable. May reference a
/// <see cref="Contact"/> by Id (optional — a meeting can stand alone). Meeting metadata (title/location/notes/
/// outcome) is kept as plain columns — a user's own scheduling data — and is scrubbed by the CRM eraser on GDPR
/// erasure. Lifecycle is a text <see cref="Status"/>: planned → done | canceled | no_show.
/// </summary>
internal sealed class Meeting : AuditableEntity, ITenantScoped, IUserOwned, ISoftDeletable
{
    public Guid UserId { get; set; }
    public Guid? ContactId { get; set; }

    public string Title { get; set; } = string.Empty;
    public DateTimeOffset ScheduledAt { get; set; }
    public int DurationMinutes { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }

    public string Status { get; set; } = MeetingStatuses.Planned;
    public string? Outcome { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>The allowed <see cref="Meeting.Status"/> values.</summary>
internal static class MeetingStatuses
{
    public const string Planned = "planned";
    public const string Done = "done";
    public const string Canceled = "canceled";
    public const string NoShow = "no_show";

    public static readonly string[] All = [Planned, Done, Canceled, NoShow];
    public static bool IsValid(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
}

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.ToTable("crm_meetings");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).HasMaxLength(256).IsRequired();
        builder.Property(m => m.Location).HasMaxLength(512);
        builder.Property(m => m.Notes).HasMaxLength(8192);
        builder.Property(m => m.Status).HasMaxLength(32).IsRequired();
        builder.Property(m => m.Outcome).HasMaxLength(8192);

        builder.HasIndex(m => new { m.UserId, m.ScheduledAt });
        builder.HasIndex(m => m.ContactId);
    }
}
