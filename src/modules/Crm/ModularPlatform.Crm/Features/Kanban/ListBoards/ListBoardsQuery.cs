using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.ListBoards;

public sealed record ListBoardsQuery(Guid UserId, int? Page, int? PageSize)
    : IQuery<PagedResponse<ModularPlatform.Crm.Features.Kanban.KanbanBoardListItem>>;
