using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Kanban.DeleteCard;

/// <summary>Soft-deletes the caller's card. Foreign/missing ⇒ 404. No event.</summary>
internal sealed class DeleteCardHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<DeleteCardCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCardCommand command, CancellationToken ct)
    {
        var card = await db.KanbanCards
            .FirstOrDefaultAsync(c => c.Id == command.CardId && c.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.card_not_found", "Card not found.");

        card.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
