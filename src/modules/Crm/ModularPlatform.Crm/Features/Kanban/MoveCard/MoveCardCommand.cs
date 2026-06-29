using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.MoveCard;

public sealed record MoveCardCommand(Guid UserId, Guid CardId, Guid ColumnId, int Position) : ICommand<Unit>;

public sealed record MoveCardRequest(Guid ColumnId, int Position);
