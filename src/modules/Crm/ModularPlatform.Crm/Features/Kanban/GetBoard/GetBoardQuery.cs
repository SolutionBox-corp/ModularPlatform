using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.GetBoard;

public sealed record GetBoardQuery(Guid UserId, Guid BoardId)
    : IQuery<ModularPlatform.Crm.Features.Kanban.KanbanBoardDetail>;
