using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Credits.GetCreditBalance;

/// <summary>
/// Read slice. Computes <c>available = posted - sum(active, non-expired holds)</c> live so an EXPIRED
/// reservation is ignored even before the sweep job runs. Never mutates; uses the no-tracking read factory.
/// </summary>
internal sealed class GetCreditBalanceHandler(
    IReadDbContextFactory<BillingDbContext> readFactory,
    IClock clock)
    : IQueryHandler<GetCreditBalanceQuery, CreditBalanceResponse>
{
    public async Task<CreditBalanceResponse> Handle(GetCreditBalanceQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var account = await db.CreditAccounts
            .Where(a => a.UserId == query.UserId)
            .Select(a => new { a.Id, a.UserId, a.Posted })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("credit.account_not_found", "Credit account not found.");

        var now = clock.UtcNow;
        var activeHolds = await db.CreditHolds
            .Where(h => h.AccountId == account.Id && h.Status == HoldStatus.Active && h.ExpiresAt > now)
            .SumAsync(h => (long?)h.Amount, ct) ?? 0L;

        var available = account.Posted - activeHolds;
        return new CreditBalanceResponse(account.Id, account.UserId, account.Posted, available);
    }
}
