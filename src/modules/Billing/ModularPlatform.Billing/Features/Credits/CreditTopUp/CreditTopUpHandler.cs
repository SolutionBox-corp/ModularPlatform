using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Credits.CreditTopUp;

/// <summary>
/// Idempotent credit top-up, EF-native. Idempotency is enforced by the UNIQUE index on
/// <c>credit_entries.idempotency_key</c>: a pre-check short-circuits a repeat, and the DB constraint is the
/// final guard if two callers race (the loser catches the unique violation and returns the already-applied
/// state). The account projection is updated on the tracked entity (xmin guards concurrent different-key
/// top-ups), a bucket + balanced entry are appended, and <see cref="CreditsToppedUpIntegrationEvent"/> is
/// published — all atomically via the outbox. No raw SQL.
/// </summary>
internal sealed class CreditTopUpHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IRealtimePublisher realtime,
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
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Lost the UNIQUE(UserId) creation race — reload the account the other writer created.
                db.Entry(account).State = EntityState.Detached;
                account = await db.CreditAccounts.FirstAsync(a => a.UserId == command.UserId, ct);
            }
        }

        if (await db.CreditEntries.AnyAsync(
                e => e.AccountId == account.Id && e.IdempotencyKey == command.IdempotencyKey, ct))
        {
            return new CreditTopUpResponse(account.Id, account.Posted, AlreadyApplied: true);
        }

        // Overflow guard (BL-11): a top-up that would wrap the projection is rejected up front; the DB CHECK
        // constraints are the backstop, but a clean 422 beats an unexpected constraint violation.
        if (account.Posted > long.MaxValue - command.Amount || account.Available > long.MaxValue - command.Amount)
        {
            throw new BusinessRuleException("credit.amount.too_large", "The top-up would overflow the balance.");
        }

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
            TransactionId = Guid.CreateVersion7(),
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
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // Lost the idempotency-key race — exactly one credit stands. Re-read the real posted balance.
            var posted = await db.CreditAccounts.AsNoTracking()
                .Where(a => a.Id == account.Id)
                .Select(a => (long?)a.Posted)
                .FirstOrDefaultAsync(ct);
            var keyApplied = await db.CreditEntries.AsNoTracking()
                .AnyAsync(e => e.AccountId == account.Id && e.IdempotencyKey == command.IdempotencyKey, ct);
            if (keyApplied && posted is not null)
            {
                return new CreditTopUpResponse(account.Id, posted.Value, AlreadyApplied: true);
            }

            throw;
        }

        // Post-commit realtime nudge so the FE refreshes the balance live (no polling). Non-transactional, so it
        // MUST fire AFTER the commit — a rolled-back top-up must not emit a phantom event. Payload is minimal: the
        // FE only uses the event TYPE to invalidate its credit query.
        await realtime.PublishToUserAsync(
            account.UserId, "billing.credits_changed", new { available = account.Available }, ct);

        return new CreditTopUpResponse(account.Id, account.Posted, AlreadyApplied: false);
    }
}
