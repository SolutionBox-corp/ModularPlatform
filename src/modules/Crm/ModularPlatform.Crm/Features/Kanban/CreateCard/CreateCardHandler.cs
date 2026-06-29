using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.CreateCard;

/// <summary>Appends a card to the bottom of a column the caller owns (position = current count). Foreign column ⇒ 404.</summary>
internal sealed class CreateCardHandler(CrmDbContext db)
    : ICommandHandler<CreateCardCommand, CreateCardResponse>
{
    public async Task<CreateCardResponse> Handle(CreateCardCommand command, CancellationToken ct)
    {
        var column = await db.KanbanColumns
            .FirstOrDefaultAsync(c => c.Id == command.ColumnId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.column_not_found", "Column not found.");

        var position = await db.KanbanCards.CountAsync(c => c.ColumnId == column.Id, ct);
        var card = new KanbanCard
        {
            UserId = command.UserId,
            BoardId = column.BoardId,
            ColumnId = column.Id,
            Position = position,
            Title = command.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(command.Description) ? null : command.Description,
            ContactId = command.ContactId,
            DealId = command.DealId,
            DueAt = command.DueAt,
        };
        db.KanbanCards.Add(card);
        await db.SaveChangesAsync(ct);
        return new CreateCardResponse(card.Id);
    }
}
