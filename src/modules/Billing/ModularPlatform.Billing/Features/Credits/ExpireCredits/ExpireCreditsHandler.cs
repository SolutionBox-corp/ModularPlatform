using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ExpireCredits;

/// <summary>
/// Sweep: materializes lapsed reservations and expired buckets into the append-only ledger. Per affected
/// account it locks the row (<c>FOR NO KEY UPDATE</c>), marks expired holds, writes Expiry entries for the
/// remaining balance of expired buckets (reducing <c>posted</c>), zeroes those buckets' remaining, and
/// recomputes the projection. Cleanup, not correctness — the availability query already ignores expired holds.
/// </summary>
internal sealed class ExpireCreditsHandler(BillingDbContext db, IClock clock)
    : ICommandHandler<ExpireCreditsCommand, ExpireCreditsResponse>
{
    public async Task<ExpireCreditsResponse> Handle(ExpireCreditsCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var expiredHoldCount = 0;
        var expiredBucketCount = 0;
        var expiredCredits = 0L;

        var accountIds = await db.CreditAccounts.Select(a => a.Id).ToListAsync(ct);

        foreach (var accountId in accountIds)
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM credit_accounts WHERE id = {accountId} FOR NO KEY UPDATE", ct);

            var account = await db.CreditAccounts.FirstAsync(a => a.Id == accountId, ct);

            var lapsedHolds = await db.CreditHolds
                .Where(h => h.AccountId == accountId && h.Status == HoldStatus.Active && h.ExpiresAt <= now)
                .ToListAsync(ct);
            foreach (var hold in lapsedHolds)
            {
                hold.Status = HoldStatus.Expired;
                hold.ResolvedAt = now;
                db.CreditEntries.Add(new CreditEntry
                {
                    AccountId = accountId,
                    Direction = CreditDirection.Credit,
                    Amount = hold.Amount,
                    TransactionId = hold.Id,
                    Type = CreditEntryType.Release,
                    BucketId = null,
                    IdempotencyKey = $"expire-hold:{hold.Id}",
                    CreatedAt = now,
                });
                expiredHoldCount++;
            }

            var expiredBuckets = await db.CreditBuckets
                .Where(b => b.AccountId == accountId && b.Remaining > 0
                    && b.ExpiresAt != null && b.ExpiresAt <= now)
                .ToListAsync(ct);
            foreach (var bucket in expiredBuckets)
            {
                var lost = bucket.Remaining;
                db.CreditEntries.Add(new CreditEntry
                {
                    AccountId = accountId,
                    Direction = CreditDirection.Debit,
                    Amount = lost,
                    TransactionId = bucket.Id,
                    Type = CreditEntryType.Expiry,
                    BucketId = bucket.Id,
                    IdempotencyKey = $"expire-bucket:{bucket.Id}",
                    CreatedAt = now,
                });
                bucket.Remaining = 0;
                account.Posted -= lost;
                expiredCredits += lost;
                expiredBucketCount++;
            }

            var activeHolds = await db.CreditHolds
                .Where(h => h.AccountId == accountId && h.Status == HoldStatus.Active && h.ExpiresAt > now)
                .SumAsync(h => (long?)h.Amount, ct) ?? 0L;
            account.Pending = activeHolds;
            account.Available = account.Posted - activeHolds;

            await db.SaveChangesAsync(ct);
        }

        return new ExpireCreditsResponse(expiredHoldCount, expiredBucketCount, expiredCredits);
    }
}
