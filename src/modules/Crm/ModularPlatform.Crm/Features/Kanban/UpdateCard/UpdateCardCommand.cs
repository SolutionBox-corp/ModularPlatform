using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.UpdateCard;

/// <summary>Partial patch for a Kanban card. Move/reorder stays in MoveCard.</summary>
public sealed record UpdateCardCommand(
    Guid UserId,
    Guid CardId,
    string? Title,
    string? Description,
    Guid? ContactId,
    Guid? DealId,
    Guid? MeetingId,
    Guid? TaskId,
    Guid? AssigneeUserId,
    string? Priority,
    string[]? Labels,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt) : ICommand<ModularPlatform.Crm.Features.Kanban.KanbanCardDto>;

public sealed record UpdateCardRequest(
    string? Title,
    string? Description,
    Guid? ContactId,
    Guid? DealId,
    Guid? MeetingId,
    Guid? TaskId,
    Guid? AssigneeUserId,
    string? Priority,
    string[]? Labels,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt);
