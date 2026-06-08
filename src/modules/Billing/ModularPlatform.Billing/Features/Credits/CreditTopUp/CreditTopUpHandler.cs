using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Credits.CreditTopUp;

/// <summary>
/// Idempotent credit top-up. The account row is locked (<c>FOR NO KEY UPDATE</c>) before mutating its
/// projection. Idempotency is enforced by the UNIQUE index on <c>credit_entries.idempotency_key</c>:
/// applying the same key twice credits exactly ONCE (pre-check inside the lock + the DB constraint as the
/// final guard). Appends a balanced ledger entry + a bucket, updates the projection, publishes the event —
/// all in ONE transaction via the outbox.
/// </summary>
internal sealed class CreditTopUpHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IClock clock)
    : ICommandHandler<CreditTopUpCommand, CreditTopUpResponse>
{
    public async Task<CreditTopUpResponse> Handle(CreditTopUpCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var now = clock.UtcNow;

        var account = await db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == command.UserId, ct);
        if (account is null)
        {
            account = new CreditAccount { UserId = command.UserId, Posted = 0, Pending = 0, Available = 0 };
            db.CreditAccounts.Add(account);
            await outbox.SaveChangesAndFlushMessagesAsync();
        }

        // Pessimistic lock on the account row for the duration of the transaction.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM credit_accounts WHERE id = {account.Id} FOR NO KEY UPDATE", ct);

        // Idempotency pre-check inside the lock: same key already applied -> return existing state, ONE credit.
        var alreadyApplied = await db.CreditEntries
            .AnyAsync(e => e.IdempotencyKey == command.IdempotencyKey, ct);
        if (alreadyApplied)
        {
            return new CreditTopUpResponse(account.Id, account.Posted, AlreadyApplied: true);
        }

        var transactionId = Guid.CreateVersion7();
        var bucket = new CreditBucket
        {
            AccountId = account.Id,
            Amount = command.Amount,
            Remaining = command.Amount,
            ExpiresAt = command.BucketExpiryDays.HasValue ? now.AddDays(command.BucketExpiryDays.Value) : null,
            CreatedAt = now,
        };
        db.CreditBuckets.Add(bucket);

        db.CreditEntries.Add(new CreditEntry
        {
            AccountId = account.Id,
            Direction = CreditDirection.Credit,
            Amount = command.Amount,
            TransactionId = transactionId,
            Type = CreditEntryType.Topup,
            BucketId = bucket.Id,
            IdempotencyKey = command.IdempotencyKey,
            CreatedAt = now,
        });

        account.Posted += command.Amount;
        account.Available += command.Amount;

        await outbox.PublishAsync(new CreditsToppedUpIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: now,
            UserId: account.UserId,
            AccountId: account.Id,
            Amount: command.Amount,
            NewPosted: account.Posted,
            IdempotencyKey: command.IdempotencyKey));

        try
        {
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost the idempotency race? If the key now exists, another writer applied it — exactly one credit stands.
            var raceLost = await db.CreditEntries.AsNoTracking()
                .AnyAsync(e => e.IdempotencyKey == command.IdempotencyKey, ct);
            if (raceLost)
            {
                return new CreditTopUpResponse(account.Id, account.Posted, AlreadyApplied: true);
            }

            throw;
        }

        return new CreditTopUpResponse(account.Id, account.Posted, AlreadyApplied: false);
    }
}
