namespace ModularPlatform.Crm.Features.Kanban;

/// <summary>Read DTOs for the Kanban feature.</summary>
public sealed record KanbanBoardListItem(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record KanbanColumnDto(Guid Id, string Name, int Position);

public sealed record KanbanCardDto(
    Guid Id,
    Guid ColumnId,
    int Position,
    string Title,
    string? Description,
    Guid? ContactId,
    Guid? DealId,
    DateTimeOffset? DueAt);

public sealed record KanbanBoardDetail(
    Guid Id,
    string Name,
    IReadOnlyList<KanbanColumnDto> Columns,
    IReadOnlyList<KanbanCardDto> Cards);
