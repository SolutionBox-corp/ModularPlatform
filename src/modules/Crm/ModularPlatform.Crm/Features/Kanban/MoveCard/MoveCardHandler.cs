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
        card.ColumnId = target.Id;

        // Reorder source (if changed) then target densely, clamping the requested slot.
        if (sourceColumnId != target.Id)
        {
            await Renumber(sourceColumnId, command.UserId, null, -1, ct);
            await Renumber(target.Id, command.UserId, card.Id, command.Position, ct);
        }
        else
        {
            await Renumber(target.Id, command.UserId, card.Id, command.Position, ct);
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private async Task Renumber(Guid columnId, Guid userId, Guid? movedCardId, int insertAt, CancellationToken ct)
    {
        var cards = await db.KanbanCards
            .Where(c => c.ColumnId == columnId && c.UserId == userId)
            .OrderBy(c => c.Position)
            .ToListAsync(ct);

        var moved = movedCardId is { } id ? cards.FirstOrDefault(c => c.Id == id) : null;
        var rest = cards.Where(c => c.Id != movedCardId).ToList();

        if (moved is not null)
        {
            var slot = Math.Clamp(insertAt, 0, rest.Count);
            rest.Insert(slot, moved);
        }

        for (var i = 0; i < rest.Count; i++)
        {
            rest[i].Position = i;
        }
    }
}
