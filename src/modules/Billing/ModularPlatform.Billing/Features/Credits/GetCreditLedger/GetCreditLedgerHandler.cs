using Microsoft.EntityFrameworkCore;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Credits.GetCreditLedger;

/// <summary>
/// Paged read of the caller's append-only credit ledger, newest first. Scoped to the caller's wallet: we resolve
/// the owner's <c>CreditAccount</c> (owner-scoped by UserId, exactly like <c>GetCreditBalance</c>) and page the
/// entries belonging to that account. No-tracking read factory; never mutates / never opens a transaction.
/// </summary>
internal sealed class GetCreditLedgerHandler(IReadDbContextFactory<BillingDbContext> readFactory)
    : IQueryHandler<GetCreditLedgerQuery, PagedResponse<CreditLedgerEntry>>
{
    public async Task<PagedResponse<CreditLedgerEntry>> Handle(GetCreditLedgerQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var accountId = await db.CreditAccounts
            .Where(a => a.UserId == query.UserId)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (accountId == Guid.Empty)
        {
            throw new NotFoundException("credit.account_not_found", "Credit account not found.");
        }

        return await db.CreditEntries
            .Where(e => e.AccountId == accountId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new CreditLedgerEntry(
                e.Id,
                e.Direction.ToString(),
                e.Amount,
                e.Type.ToString(),
                e.TransactionId,
                e.IdempotencyKey,
                e.CreatedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
