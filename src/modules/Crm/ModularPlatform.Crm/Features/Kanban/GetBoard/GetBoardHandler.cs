using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Kanban;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.GetBoard;

/// <summary>Owner-scoped board with its columns (by position) and cards (by column+position). Foreign/missing ⇒ 404.</summary>
internal sealed class GetBoardHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<GetBoardQuery, KanbanBoardDetail>
{
    public async Task<KanbanBoardDetail> Handle(GetBoardQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var board = await db.KanbanBoards
            .Where(b => b.Id == query.BoardId && b.UserId == query.UserId)
            .Select(b => new { b.Id, b.Name })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("crm.board_not_found", "Board not found.");

        var columns = await db.KanbanColumns
            .Where(c => c.BoardId == board.Id && c.UserId == query.UserId)
            .OrderBy(c => c.Position)
            .Select(c => new KanbanColumnDto(c.Id, c.Name, c.Position))
            .ToListAsync(ct);

        var cards = await db.KanbanCards
            .Where(c => c.BoardId == board.Id && c.UserId == query.UserId)
            .OrderBy(c => c.Position)
            .Select(c => new KanbanCardDto(c.Id, c.ColumnId, c.Position, c.Title, c.Description, c.ContactId, c.DealId, c.DueAt))
            .ToListAsync(ct);

        return new KanbanBoardDetail(board.Id, board.Name, columns, cards);
    }
}
