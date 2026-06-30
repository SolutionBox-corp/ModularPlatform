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
    private const int BatchSize = 100;

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

        foreach (var batch in accountIds.Chunk(BatchSize))
        {
            var batchResult = await ProcessBatchAsync(batch, now, ct);
            expiredHoldCount += batchResult.ExpiredHolds;
            expiredBucketCount += batchResult.ExpiredBuckets;
            expiredCredits += batchResult.ExpiredCredits;
        }

        return new ExpireCreditsResponse(expiredHoldCount, expiredBucketCount, expiredCredits);
    }

    private async Task<ExpireCreditsResponse> ProcessBatchAsync(
        IReadOnlyList<Guid> accountIds,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var expiredHoldCount = 0;
        var expiredBucketCount = 0;
        var expiredCredits = 0L;

        var accounts = await db.CreditAccounts
            .Where(a => accountIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var lapsedHolds = (await db.CreditHolds
                .Where(h => accountIds.Contains(h.AccountId) && h.Status == HoldStatus.Active && h.ExpiresAt <= now)
                .ToListAsync(ct))
            .GroupBy(h => h.AccountId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CreditHold>)g.ToList());
        var expiredBuckets = (await db.CreditBuckets
                .Where(b => accountIds.Contains(b.AccountId)
                            && b.Remaining > 0
                            && b.ExpiresAt != null
                            && b.ExpiresAt <= now)
                .OrderBy(b => b.ExpiresAt)
                .ToListAsync(ct))
            .GroupBy(b => b.AccountId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<CreditBucket>)g.ToList());

        for (var index = 0; index < accountIds.Count; index++)
        {
            var accountId = accountIds[index];
            if (!accounts.TryGetValue(accountId, out var account))
            {
                continue;
            }

            try
            {
                var result = await ApplyAndSaveAsync(
                    account,
                    lapsedHolds.GetValueOrDefault(accountId) ?? [],
                    expiredBuckets.GetValueOrDefault(accountId) ?? [],
                    now,
                    ct);
                expiredHoldCount += result.ExpiredHolds;
                expiredBucketCount += result.ExpiredBuckets;
                expiredCredits += result.ExpiredCredits;
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                // Per-account isolation: one account's unexpected persistence failure must NOT abort the
                // platform-wide sweep. Clearing the tracker discards this account's failed pending changes; the
                // remaining accounts in the batch fall back to isolated per-account reads so they can still finish.
                // Concurrency conflicts deliberately bubble to ConcurrencyRetryBehavior.
                db.ChangeTracker.Clear();

                for (var fallbackIndex = index + 1; fallbackIndex < accountIds.Count; fallbackIndex++)
                {
                    var fallbackResult = await ProcessSingleAccountAsync(accountIds[fallbackIndex], now, ct);
                    expiredHoldCount += fallbackResult.ExpiredHolds;
                    expiredBucketCount += fallbackResult.ExpiredBuckets;
                    expiredCredits += fallbackResult.ExpiredCredits;
                }

                return new ExpireCreditsResponse(expiredHoldCount, expiredBucketCount, expiredCredits);
            }
        }

        db.ChangeTracker.Clear();
        return new ExpireCreditsResponse(expiredHoldCount, expiredBucketCount, expiredCredits);
    }

    private async Task<ExpireCreditsResponse> ProcessSingleAccountAsync(
        Guid accountId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        try
        {
            var account = await db.CreditAccounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (account is null)
            {
                return new ExpireCreditsResponse(0, 0, 0);
            }

            var lapsedHolds = await db.CreditHolds
                .Where(h => h.AccountId == accountId && h.Status == HoldStatus.Active && h.ExpiresAt <= now)
                .ToListAsync(ct);
            var expiredBuckets = await db.CreditBuckets
                .Where(b => b.AccountId == accountId && b.Remaining > 0 && b.ExpiresAt != null && b.ExpiresAt <= now)
                .OrderBy(b => b.ExpiresAt)
                .ToListAsync(ct);

            return await ApplyAndSaveAsync(account, lapsedHolds, expiredBuckets, now, ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return new ExpireCreditsResponse(0, 0, 0);
        }
    }

    private async Task<ExpireCreditsResponse> ApplyAndSaveAsync(
        CreditAccount account,
        IReadOnlyList<CreditHold> lapsedHolds,
        IReadOnlyList<CreditBucket> expiredBuckets,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var accountExpiredHoldCount = 0;
        var accountExpiredBucketCount = 0;
        var accountExpiredCredits = 0L;

        foreach (var hold in lapsedHolds)
        {
            hold.Status = HoldStatus.Expired;
            hold.ResolvedAt = now;
            db.CreditEntries.Add(new CreditEntry
            {
                AccountId = account.Id,
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

        foreach (var bucket in expiredBuckets)
        {
            // Only destroy credits that are actually FREE. A reservation decrements Available/raises Pending but
            // does NOT draw its backing bucket, so bucket.Remaining can still count credits committed to an active
            // hold. account.Available IS the free balance (= Posted - Pending); a bucket whose Remaining exceeds
            // it is (partly) reserved — skip it FULLY this sweep (keeps the expire-bucket:{id} idempotency key
            // unique) and expire it on a later sweep once the hold resolves.
            if (bucket.Remaining > account.Available)
            {
                continue;
            }

            var lost = bucket.Remaining;
            db.CreditEntries.Add(new CreditEntry
            {
                AccountId = account.Id,
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
        return new ExpireCreditsResponse(accountExpiredHoldCount, accountExpiredBucketCount, accountExpiredCredits);
    }
}
