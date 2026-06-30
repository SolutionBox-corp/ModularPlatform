using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Json;

namespace ModularPlatform.Crm.Features.Kanban.UpdateCard;

/// <summary>Partial patch for a Kanban card. Move/reorder stays in MoveCard.</summary>
public sealed record UpdateCardCommand(
    Guid UserId,
    Guid CardId,
    string? Title,
    string? Description,
    Guid? ContactId,
    bool ContactIdSet,
    Guid? DealId,
    bool DealIdSet,
    Guid? MeetingId,
    bool MeetingIdSet,
    Guid? TaskId,
    bool TaskIdSet,
    Guid? AssigneeUserId,
    bool AssigneeUserIdSet,
    string? Priority,
    string[]? Labels,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt) : ICommand<ModularPlatform.Crm.Features.Kanban.KanbanCardDto>;

public sealed record UpdateCardRequest(
    string? Title,
    string? Description,
    Optional<Guid?> ContactId,
    Optional<Guid?> DealId,
    Optional<Guid?> MeetingId,
    Optional<Guid?> TaskId,
    Optional<Guid?> AssigneeUserId,
    string? Priority,
    string[]? Labels,
    DateTimeOffset? StartAt,
    DateTimeOffset? DueAt);
