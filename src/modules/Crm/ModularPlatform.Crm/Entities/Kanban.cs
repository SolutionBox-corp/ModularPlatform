using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence.Entities;

namespace ModularPlatform.Crm.Entities;

/// <summary>A kanban board owned by a user (per-user RLS), tenant-scoped, soft-deletable. Name is [PersonalData].</summary>
internal sealed class KanbanBoard : AuditableEntity, ITenantScoped, IUserOwned, ISoftDeletable, IDataSubject
{
    public Guid UserId { get; set; }

    [PersonalData]
    public string Name { get; set; } = string.Empty;

    Guid IDataSubject.SubjectId => UserId;
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>A column on a board (To Do / In Progress / Done); ordered by Position within its board.</summary>
internal sealed class KanbanColumn : AuditableEntity, ITenantScoped, IUserOwned, IDataSubject
{
    public Guid UserId { get; set; }
    public Guid BoardId { get; set; }

    [PersonalData]
    public string Name { get; set; } = string.Empty;

    public int Position { get; set; }

    Guid IDataSubject.SubjectId => UserId;
}

/// <summary>A card in a column; ordered by Position. May reference a contact/deal by Id. Title/Description PII.</summary>
internal sealed class KanbanCard : AuditableEntity, ITenantScoped, IUserOwned, ISoftDeletable, IDataSubject
{
    public Guid UserId { get; set; }
    public Guid BoardId { get; set; }
    public Guid ColumnId { get; set; }
    public int Position { get; set; }

    [PersonalData]
    public string Title { get; set; } = string.Empty;

    [PersonalData]
    public string? Description { get; set; }

    public Guid? ContactId { get; set; }
    public Guid? DealId { get; set; }
    public DateTimeOffset? DueAt { get; set; }

    Guid IDataSubject.SubjectId => UserId;
    public DateTimeOffset? DeletedAt { get; set; }
}

internal sealed class KanbanBoardConfiguration : IEntityTypeConfiguration<KanbanBoard>
{
    public void Configure(EntityTypeBuilder<KanbanBoard> b)
    {
        b.ToTable("crm_kanban_boards");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(256).IsRequired();
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

internal sealed class KanbanColumnConfiguration : IEntityTypeConfiguration<KanbanColumn>
{
    public void Configure(EntityTypeBuilder<KanbanColumn> b)
    {
        b.ToTable("crm_kanban_columns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.BoardId, x.Position });
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class KanbanCardConfiguration : IEntityTypeConfiguration<KanbanCard>
{
    public void Configure(EntityTypeBuilder<KanbanCard> b)
    {
        b.ToTable("crm_kanban_cards");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(256).IsRequired();
        b.Property(x => x.Description).HasMaxLength(8192);
        b.HasIndex(x => new { x.ColumnId, x.Position });
        b.HasIndex(x => x.BoardId);
        b.HasIndex(x => x.UserId);
    }
}
