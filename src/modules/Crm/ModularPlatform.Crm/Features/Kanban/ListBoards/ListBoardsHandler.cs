using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Kanban;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.ListBoards;

internal sealed class ListBoardsHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<ListBoardsQuery, PagedResponse<KanbanBoardListItem>>
{
    public async Task<PagedResponse<KanbanBoardListItem>> Handle(ListBoardsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();
        var paging = new PageRequest(query.Page, query.PageSize);
        var filtered = db.KanbanBoards.Where(b => b.UserId == query.UserId);
        var total = await filtered.CountAsync(ct);
        var items = await filtered
            .OrderByDescending(b => b.CreatedAt)
            .Skip(paging.Skip).Take(paging.PageSize)
            .Select(b => new KanbanBoardListItem(b.Id, b.Name, b.CreatedAt))
            .ToListAsync(ct);
        return new PagedResponse<KanbanBoardListItem>(items, paging.Page, paging.PageSize, total);
    }
}
