using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>
/// A work note/comment on a CRM task. Owned by the caller, tenant-scoped, and encrypted at rest because notes can
/// contain customer PII or sensitive sales context.
/// </summary>
internal sealed class CrmTaskComment : AuditableEntity, ITenantScoped, IUserOwned, IDataSubject
{
    public Guid UserId { get; set; }
    public Guid TaskId { get; set; }

    [PersonalData]
    [Encrypted]
    public string Body { get; set; } = string.Empty;

    Guid IDataSubject.SubjectId => UserId;
}

internal sealed class CrmTaskCommentConfiguration : IEntityTypeConfiguration<CrmTaskComment>
{
    public void Configure(EntityTypeBuilder<CrmTaskComment> builder)
    {
        builder.ToTable("crm_task_comments");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Body);
        builder.HasIndex(c => new { c.TaskId, c.CreatedAt });
        builder.HasIndex(c => c.UserId);
    }
}
