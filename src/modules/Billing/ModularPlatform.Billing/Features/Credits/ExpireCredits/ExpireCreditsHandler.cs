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

        var holdAccountIds = db.CreditHolds
            .Where(h => h.Status == HoldStatus.Active && h.ExpiresAt <= now)
            .Select(h => h.AccountId);
        var bucketAccountIds = db.CreditBuckets
            .Where(b => b.Remaining > 0 && b.ExpiresAt != null && b.ExpiresAt <= now)
            .Select(b => b.AccountId);
        var accountIds = await holdAccountIds
            .Union(bucketAccountIds)
            .OrderBy(id => id)
            .ToListAsync(ct);

        foreach (var accountId in accountIds)
        {
            try
            {
                var accountExpiredHoldCount = 0;
                var accountExpiredBucketCount = 0;
                var accountExpiredCredits = 0L;
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
                    accountExpiredHoldCount++;
                }

                var expiredBuckets = await db.CreditBuckets
                    .Where(b => b.AccountId == accountId && b.Remaining > 0 && b.ExpiresAt != null && b.ExpiresAt <= now)
                    .OrderBy(b => b.ExpiresAt)
                    .ToListAsync(ct);
                foreach (var bucket in expiredBuckets)
                {
                    // Only destroy credits that are actually FREE. A reservation decrements Available/raises Pending
                    // but does NOT draw its backing bucket, so bucket.Remaining can still count credits committed to
                    // an active hold. account.Available IS the free balance (= Posted - Pending); a bucket whose
                    // Remaining exceeds it is (partly) reserved — skip it FULLY this sweep (keeps the
                    // expire-bucket:{id} idempotency key unique) and expire it on a later sweep once the hold
                    // resolves. Destroying the held portion would drive Available negative (CHECK violation) and
                    // break the eventual confirm.
                    if (bucket.Remaining > account.Available)
                    {
                        continue;
                    }

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
                    accountExpiredCredits += lost;
                    accountExpiredBucketCount++;
                }

                await db.SaveChangesAsync(ct);
                expiredHoldCount += accountExpiredHoldCount;
                expiredBucketCount += accountExpiredBucketCount;
                expiredCredits += accountExpiredCredits;
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                // Per-account isolation: one account's unexpected persistence failure must NOT abort the
                // platform-wide sweep (the old code let a single bad account skip every account ordered after it).
                // Discard its tracked changes and continue — the next sweep retries it. Concurrency conflicts are
                // deliberately NOT caught here: they bubble to ConcurrencyRetryBehavior, which retries the whole
                // sweep (idempotent — the expire-*:{id} keys dedup already-applied accounts).
                db.ChangeTracker.Clear();
            }
        }

        return new ExpireCreditsResponse(expiredHoldCount, expiredBucketCount, expiredCredits);
    }
}
