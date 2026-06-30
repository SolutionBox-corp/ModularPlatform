namespace ModularPlatform.Crm.Features.Kanban;

/// <summary>Read DTOs for the Kanban feature.</summary>
public sealed record KanbanBoardListItem(Guid Id, string Name, DateTimeOffset CreatedAt);

public sealed record KanbanColumnDto(
    Guid Id,
    string Name,
    int Position,
    string Color,
    string Group,
    bool IsDefault,
    int? WipLimit);

public sealed record KanbanCardDto(
    Guid Id,
    Guid ColumnId,
    int Position,
    string Title,
    string? Description,
    Guid? ContactId,
    Guid? DealId,
    string? DealTitle,
    long? DealAmountCents,
    string? DealCurrency,
    Guid? MeetingId,
    Guid? TaskId,
    Guid? AssigneeUserId,
    string Priority,
    string[] Labels,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt);

public sealed record KanbanBoardDetail(
    Guid Id,
    string Name,
    IReadOnlyList<KanbanColumnDto> Columns,
    IReadOnlyList<KanbanCardDto> Cards);
