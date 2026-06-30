using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A follow-up task / reminder owned by a user (per-user RLS), tenant-scoped, soft-deletable. May reference a
/// <see cref="Contact"/> and/or <see cref="Deal"/> by Id (optional, no navigation). Title/Description are plain text
/// but [PersonalData] so audited values crypto-shred under the user's DEK; the eraser scrubs the live row. Lifecycle
/// is open → done (CompletedAt set). The "what to do today" query filters DueAt &lt;= now on open tasks.
/// </summary>
internal sealed class CrmTask : AuditableEntity, ITenantScoped, IUserOwned, ISoftDeletable, IDataSubject
{
    public Guid UserId { get; set; }
    public Guid? ContactId { get; set; }
    public Guid? DealId { get; set; }
    public Guid? AssigneeUserId { get; set; }

    [PersonalData]
    public string Title { get; set; } = string.Empty;

    [PersonalData]
    public string? Description { get; set; }

    public DateTimeOffset? DueAt { get; set; }
    public string Priority { get; set; } = TaskPriorities.Normal;
    public string Status { get; set; } = TaskStatuses.Open;
    public DateTimeOffset? CompletedAt { get; set; }

    Guid IDataSubject.SubjectId => UserId;

    public DateTimeOffset? DeletedAt { get; set; }
}

internal static class TaskPriorities
{
    public const string Low = "low";
    public const string Normal = "normal";
    public const string High = "high";

    public static readonly string[] All = [Low, Normal, High];
    public static bool IsValid(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
}

internal static class TaskStatuses
{
    public const string Open = "open";
    public const string Done = "done";

    public static readonly string[] All = [Open, Done];
    public static bool IsValid(string? value) => value is not null && Array.IndexOf(All, value) >= 0;
}

internal sealed class CrmTaskConfiguration : IEntityTypeConfiguration<CrmTask>
{
    public void Configure(EntityTypeBuilder<CrmTask> builder)
    {
        builder.ToTable("crm_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Title).HasMaxLength(256).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(8192);
        builder.Property(t => t.Priority).HasMaxLength(16).IsRequired();
        builder.Property(t => t.Status).HasMaxLength(16).IsRequired();

        builder.HasIndex(t => new { t.UserId, t.Status, t.DueAt });
        builder.HasIndex(t => t.ContactId);
        builder.HasIndex(t => t.DealId);
        builder.HasIndex(t => t.AssigneeUserId);
    }
}
