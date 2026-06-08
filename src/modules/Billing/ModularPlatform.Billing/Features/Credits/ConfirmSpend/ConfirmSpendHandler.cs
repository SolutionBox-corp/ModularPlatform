using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Credits.ConfirmSpend;

/// <summary>
/// Confirms an active reservation into a POSTED debit. Pessimistic: locks the account row, validates the
/// hold is still Active and not expired, appends a balanced Spend entry, draws buckets soonest-to-expire
/// (FIFO), decrements <c>posted</c>, marks the hold Confirmed, and publishes <see cref="CreditsSpentIntegrationEvent"/>
/// — all in ONE transaction via the outbox. Idempotent: a hold already Confirmed returns its current state.
/// </summary>
internal sealed class ConfirmSpendHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    IClock clock)
    : ICommandHandler<ConfirmSpendCommand, ConfirmSpendResponse>
{
    public async Task<ConfirmSpendResponse> Handle(ConfirmSpendCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var now = clock.UtcNow;

        var account = await db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == command.UserId, ct)
            ?? throw new NotFoundException("credit.account_not_found", "Credit account not found.");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM credit_accounts WHERE id = {account.Id} FOR NO KEY UPDATE", ct);

        var hold = await db.CreditHolds
            .FirstOrDefaultAsync(h => h.Id == command.ReservationId && h.AccountId == account.Id, ct)
            ?? throw new NotFoundException("credit.reservation_not_found", "Reservation not found.");

        if (hold.Status == HoldStatus.Confirmed)
        {
            return new ConfirmSpendResponse(account.Id, account.Posted, account.Available);
        }

        if (hold.Status != HoldStatus.Active || hold.ExpiresAt <= now)
        {
            throw new BusinessRuleException(
                "credit.reservation_not_active", "Reservation is no longer active.");
        }

        // Draw buckets soonest-to-expire first (FIFO over expiry).
        var remaining = hold.Amount;
        var buckets = await db.CreditBuckets
            .Where(b => b.AccountId == account.Id && b.Remaining > 0)
            .OrderBy(b => b.ExpiresAt == null)
            .ThenBy(b => b.ExpiresAt)
            .ThenBy(b => b.CreatedAt)
            .ToListAsync(ct);

        foreach (var bucket in buckets)
        {
            if (remaining <= 0)
            {
                break;
            }

            var draw = Math.Min(bucket.Remaining, remaining);
            bucket.Remaining -= draw;
            remaining -= draw;
        }

        db.CreditEntries.Add(new CreditEntry
        {
            AccountId = account.Id,
            Direction = CreditDirection.Debit,
            Amount = hold.Amount,
            TransactionId = hold.Id,
            Type = CreditEntryType.Spend,
            BucketId = null,
            IdempotencyKey = $"spend:{hold.Id}",
            CreatedAt = now,
        });

        hold.Status = HoldStatus.Confirmed;
        hold.ResolvedAt = now;

        account.Posted -= hold.Amount;
        var activeHolds = await db.CreditHolds
            .Where(h => h.AccountId == account.Id && h.Status == HoldStatus.Active && h.ExpiresAt > now)
            .SumAsync(h => (long?)h.Amount, ct) ?? 0L;
        account.Pending = activeHolds;
        account.Available = account.Posted - activeHolds;

        await outbox.PublishAsync(new CreditsSpentIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: now,
            UserId: account.UserId,
            AccountId: account.Id,
            ReservationId: hold.Id,
            Amount: hold.Amount,
            NewPosted: account.Posted));

        await outbox.SaveChangesAndFlushMessagesAsync();

        return new ConfirmSpendResponse(account.Id, account.Posted, account.Available);
    }
}
