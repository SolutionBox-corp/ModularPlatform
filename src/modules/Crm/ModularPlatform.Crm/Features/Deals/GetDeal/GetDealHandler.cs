using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Deals.GetDeal;

/// <summary>Read slice (no-tracking). Owner-scoped by WHERE + RLS; foreign/missing ⇒ 404 (leaks nothing).</summary>
internal sealed class GetDealHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<GetDealQuery, DealResponse>
{
    public async Task<DealResponse> Handle(GetDealQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        return await db.Deals
            .Where(d => d.Id == query.DealId && d.UserId == query.UserId)
            .Select(d => new DealResponse(
                d.Id, d.ContactId, d.CompanyId, d.Title, d.AmountCents, d.Currency, d.Stage, d.LastStage,
                d.ProbabilityPercent, d.LeadSource, d.ExpectedCloseAt, d.ClosedAt, d.NextStep, d.Notes,
                d.CreatedAt, d.UpdatedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("crm.deal_not_found", "Deal not found.");
    }
}
