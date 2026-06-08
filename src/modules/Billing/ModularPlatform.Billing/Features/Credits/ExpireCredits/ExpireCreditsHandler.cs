using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ExpireCredits;

/// <summary>
/// Sweep: materializes lapsed reservations and expired buckets into the append-only ledger and the account
/// projection. EF-native + tracked (xmin) per account — a concurrent mutation conflicts and is retried. Keeps
/// the invariant <c>available = posted - pending</c>: an expired hold restores availability; an expired bucket
/// destroys credits (posted and available drop). No raw SQL.
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
                account.Available += hold.Amount;
                account.Pending -= hold.Amount;
                expiredHoldCount++;
            }

            var expiredBuckets = await db.CreditBuckets
                .Where(b => b.AccountId == accountId && b.Remaining > 0 && b.ExpiresAt != null && b.ExpiresAt <= now)
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
                account.Available -= lost;
                expiredCredits += lost;
                expiredBucketCount++;
            }

            await db.SaveChangesAsync(ct);
        }

        return new ExpireCreditsResponse(expiredHoldCount, expiredBucketCount, expiredCredits);
    }
}
