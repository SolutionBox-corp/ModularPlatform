using Microsoft.EntityFrameworkCore;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Credits.GetCreditBalance;

/// <summary>
/// Read slice. Returns the authoritative stored projection (<c>posted</c>/<c>pending</c>/<c>available</c>) so the displayed
/// balance matches exactly what a reservation will allow. Expired-but-not-yet-swept holds keep <c>available</c>
/// slightly conservative until the expiry sweep reconciles them. Uses the no-tracking read factory; never mutates.
/// </summary>
internal sealed class GetCreditBalanceHandler(IReadDbContextFactory<BillingDbContext> readFactory)
    : IQueryHandler<GetCreditBalanceQuery, CreditBalanceResponse>
{
    public async Task<CreditBalanceResponse> Handle(GetCreditBalanceQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        return await db.CreditAccounts
            .Where(a => a.UserId == query.UserId)
            .Select(a => new CreditBalanceResponse(a.Id, a.UserId, a.Posted, a.Pending, a.Available))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("credit.account_not_found", "Credit account not found.");
    }
}
