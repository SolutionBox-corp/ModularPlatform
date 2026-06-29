using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Deals.MoveDealStage;

/// <summary>
/// Advances the caller's deal to a new pipeline stage. Idempotent (same stage = no-op); a deal already in a terminal
/// stage (won/lost) cannot move again — closed is closed. Entering a terminal stage records ClosedAt; xmin +
/// ConcurrencyRetryBehavior serialize concurrent moves. Foreign/missing ⇒ 404.
/// </summary>
internal sealed class MoveDealStageHandler(CrmDbContext db, IClock clock)
    : ICommandHandler<MoveDealStageCommand, DealResponse>
{
    public async Task<DealResponse> Handle(MoveDealStageCommand command, CancellationToken ct)
    {
        var deal = await db.Deals
            .FirstOrDefaultAsync(d => d.Id == command.DealId && d.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.deal_not_found", "Deal not found.");

        if (deal.Stage == command.Stage)
        {
            return ToResponse(deal);
        }

        if (DealStages.IsTerminal(deal.Stage))
        {
            throw new BusinessRuleException("crm.deal.invalid_transition", "A closed deal cannot change stage.");
        }

        deal.Stage = command.Stage;
        deal.ClosedAt = DealStages.IsTerminal(command.Stage) ? clock.UtcNow : null;

        await db.SaveChangesAsync(ct);

        return ToResponse(deal);
    }

    private static DealResponse ToResponse(Deal deal) => new(
        deal.Id, deal.ContactId, deal.CompanyId, deal.Title, deal.AmountCents, deal.Currency, deal.Stage, deal.ExpectedCloseAt,
        deal.ClosedAt, deal.Notes, deal.CreatedAt, deal.UpdatedAt);
}
