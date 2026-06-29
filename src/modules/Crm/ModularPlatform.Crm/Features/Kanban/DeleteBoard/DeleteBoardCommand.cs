using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.DeleteBoard;

public sealed record DeleteBoardCommand(Guid UserId, Guid BoardId) : ICommand<Unit>;
