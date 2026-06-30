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

        // CreatedAt + Id tiebreakers make the order deterministic even if two concurrent appends raced to the same
        // Position (cosmetic duplicate positions then render in stable insertion order instead of arbitrarily).
        var columns = await db.KanbanColumns
            .Where(c => c.BoardId == board.Id && c.UserId == query.UserId)
            .OrderBy(c => c.Position).ThenBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new KanbanColumnDto(c.Id, c.Name, c.Position, c.Color, c.Group, c.IsDefault, c.WipLimit))
            .ToListAsync(ct);

        var cards = await (
                from card in db.KanbanCards.Where(c => c.BoardId == board.Id && c.UserId == query.UserId)
                join deal in db.Deals.Where(d => d.UserId == query.UserId)
                    on card.DealId equals deal.Id into dealGroup
                from deal in dealGroup.DefaultIfEmpty()
                orderby card.Position, card.CreatedAt, card.Id
                select new KanbanCardDto(
                    card.Id, card.ColumnId, card.Position, card.Title, card.Description, card.ContactId, card.DealId,
                    deal == null ? null : deal.Title,
                    deal == null ? null : deal.AmountCents,
                    deal == null ? null : deal.Currency,
                    card.MeetingId, card.TaskId, card.AssigneeUserId, card.Priority, card.Labels, card.StartAt, card.DueAt))
            .ToListAsync(ct);

        return new KanbanBoardDetail(board.Id, board.Name, columns, cards);
    }
}
