using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.CreateCard;

public sealed record CreateCardCommand(
    Guid UserId,
    Guid BoardId,
    Guid ColumnId,
    string Title,
    string? Description,
    Guid? ContactId,
    Guid? DealId,
    DateTimeOffset? DueAt) : ICommand<CreateCardResponse>;

public sealed record CreateCardResponse(Guid Id);

public sealed record CreateCardRequest(
    Guid ColumnId,
    string Title,
    string? Description,
    Guid? ContactId,
    Guid? DealId,
    DateTimeOffset? DueAt);
