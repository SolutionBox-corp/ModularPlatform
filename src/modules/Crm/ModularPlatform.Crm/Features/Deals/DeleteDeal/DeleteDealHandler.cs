using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Deals.DeleteDeal;

/// <summary>Soft-deletes a tracked deal owned by the caller. Foreign/missing ⇒ 404. No event is published.</summary>
internal sealed class DeleteDealHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<DeleteDealCommand, Unit>
{
    public async Task<Unit> Handle(DeleteDealCommand command, CancellationToken ct)
    {
        var deal = await db.Deals
            .FirstOrDefaultAsync(d => d.Id == command.DealId && d.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.deal_not_found", "Deal not found.");

        deal.DeletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
