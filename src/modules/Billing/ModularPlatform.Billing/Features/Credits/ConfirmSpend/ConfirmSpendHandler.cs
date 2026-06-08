using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Credits.ConfirmSpend;

/// <summary>
/// Confirms an active reservation into a posted spend. EF-native concurrency: the hold and account are tracked,
/// so the xmin concurrency token serializes a double-confirm (a second concurrent confirm conflicts and is
/// retried by ConcurrencyRetryBehavior, then sees the hold already Confirmed and returns idempotently). Draws
/// buckets soonest-to-expire (FIFO), keeps the invariant <c>available = posted - pending</c>, and publishes
/// <see cref="CreditsSpentIntegrationEvent"/> atomically via the outbox. No raw SQL.
/// </summary>
internal sealed class ConfirmSpendHandler(IDbContextOutbox<BillingDbContext> outbox, IClock clock)
    : ICommandHandler<ConfirmSpendCommand, ConfirmSpendResponse>
{
    public async Task<ConfirmSpendResponse> Handle(ConfirmSpendCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var now = clock.UtcNow;

        var account = await db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == command.UserId, ct)
            ?? throw new NotFoundException("credit.account_not_found", "Credit account not found.");

        var hold = await db.CreditHolds
            .FirstOrDefaultAsync(h => h.Id == command.ReservationId && h.AccountId == account.Id, ct)
            ?? throw new NotFoundException("credit.reservation_not_found", "Reservation not found.");

        if (hold.Status == HoldStatus.Confirmed)
        {
            return new ConfirmSpendResponse(account.Id, account.Posted, account.Available);
        }

        if (hold.Status != HoldStatus.Active || hold.ExpiresAt <= now)
        {
            throw new BusinessRuleException("credit.reservation_not_active", "Reservation is no longer active.");
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

        // The held credits are now spent: posted and pending both drop by the amount; available is unchanged
        // (it was already reduced at reservation). Invariant available = posted - pending is preserved.
        account.Posted -= hold.Amount;
        account.Pending -= hold.Amount;

        await outbox.PublishAsync(new CreditsSpentIntegrationEvent(
            EventId: Guid.CreateVersion7(),
            OccurredAt: now,
            UserId: account.UserId,
            AccountId: account.Id,
            ReservationId: hold.Id,
            Amount: hold.Amount,
            NewPosted: account.Posted));

        try
        {
            // Wolverine saves all tracked changes (xmin-checked) + the outbox event in one transaction.
            await outbox.SaveChangesAndFlushMessagesAsync();
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // A concurrent confirm of the same reservation already posted the spend (UNIQUE spend:{holdId}).
            // Idempotent: report the now-committed state.
            if (await db.CreditEntries.AsNoTracking().AnyAsync(e => e.IdempotencyKey == $"spend:{hold.Id}", ct))
            {
                var current = await db.CreditAccounts.AsNoTracking()
                    .Where(a => a.Id == account.Id)
                    .Select(a => new { a.Posted, a.Available })
                    .FirstAsync(ct);
                return new ConfirmSpendResponse(account.Id, current.Posted, current.Available);
            }

            throw;
        }

        return new ConfirmSpendResponse(account.Id, account.Posted, account.Available);
    }
}
