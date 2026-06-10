using Microsoft.EntityFrameworkCore;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Features.Purchases.GetCreditPurchase;

/// <summary>
/// Purchase status from the saga state row, which deliberately persists past resolution (the saga never
/// calls <c>MarkCompleted</c> — the row IS the user-facing purchase record). Owner-scoped: the caller sees
/// only their purchase, anything else is a 404 (no existence oracle). The row appears asynchronously — the
/// Worker materializes it from <c>CreditPurchaseStarted</c> moments after checkout creation.
/// </summary>
internal sealed class GetCreditPurchaseHandler(IReadDbContextFactory<BillingDbContext> readFactory)
    : IQueryHandler<GetCreditPurchaseQuery, CreditPurchaseResponse>
{
    public async Task<CreditPurchaseResponse> Handle(GetCreditPurchaseQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        return await db.CreditPurchaseSagas.AsNoTracking()
            .Where(s => s.Id == query.PurchaseId && s.UserId == query.UserId)
            .Select(s => new CreditPurchaseResponse(
                s.Id, s.PackageId, s.CreditAmount, s.Status, s.StartedAt, s.ResolvedAt))
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("billing.purchase_not_found", "Purchase not found.");
    }
}
