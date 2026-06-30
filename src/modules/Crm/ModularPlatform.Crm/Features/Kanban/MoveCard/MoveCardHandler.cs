using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.MoveCard;

/// <summary>
/// Moves the caller's card to a target column + position, renumbering the affected columns densely (0..n). The
/// target column must be on the same board and owned. xmin + ConcurrencyRetryBehavior serialize concurrent moves;
/// EF/LINQ only. Foreign card/column ⇒ 404.
/// </summary>
internal sealed class MoveCardHandler(CrmDbContext db)
    : ICommandHandler<MoveCardCommand, Unit>
{
    public async Task<Unit> Handle(MoveCardCommand command, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .FirstOrDefaultAsync(c => c.Id == command.CardId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.card_not_found", "Card not found.");

        var target = await db.KanbanColumns
            .FirstOrDefaultAsync(c => c.Id == command.ColumnId && c.UserId == command.UserId && c.BoardId == card.BoardId, ct)
            ?? throw new NotFoundException("crm.column_not_found", "Column not found.");

        var sourceColumnId = card.ColumnId;

        // Renumber against the IN-MEMORY card lists, not a post-mutation DB query: the moved card's ColumnId change
        // is not yet persisted, so a `Where(ColumnId == …)` would still see the old column. Load both columns, move the
        // tracked card between the in-memory lists, then assign dense 0..n positions. xmin + ConcurrencyRetryBehavior
        // serialize concurrent moves.
        if (sourceColumnId == target.Id)
        {
            var cards = await LoadColumn(target.Id, command.UserId, ct);
            cards.RemoveAll(c => c.Id == card.Id);
            cards.Insert(Math.Clamp(command.Position, 0, cards.Count), card);
            Renumber(cards);
        }
        else
        {
            var sourceCards = await LoadColumn(sourceColumnId, command.UserId, ct);
            var targetCards = await LoadColumn(target.Id, command.UserId, ct);

            sourceCards.RemoveAll(c => c.Id == card.Id);
            card.ColumnId = target.Id;
            targetCards.Insert(Math.Clamp(command.Position, 0, targetCards.Count), card);

            Renumber(sourceCards);
            Renumber(targetCards);
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private Task<List<Entities.KanbanCard>> LoadColumn(Guid columnId, Guid userId, CancellationToken ct) =>
        db.KanbanCards
            .Where(c => c.ColumnId == columnId && c.UserId == userId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

    private static void Renumber(List<Entities.KanbanCard> cards)
    {
        for (var i = 0; i < cards.Count; i++)
        {
            cards[i].Position = i;
        }
    }
}
