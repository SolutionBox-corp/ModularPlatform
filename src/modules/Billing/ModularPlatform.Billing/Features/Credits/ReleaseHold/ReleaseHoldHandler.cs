using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ReleaseHold;

/// <summary>
/// Releases an active reservation, restoring availability. Pessimistic: locks the account row, appends a
/// balanced Release entry, marks the hold Released, and recomputes the projection. Idempotent: an already
/// resolved hold returns current state. No integration event, so it injects the scoped DbContext directly.
/// </summary>
internal sealed class ReleaseHoldHandler(BillingDbContext db, IClock clock)
    : ICommandHandler<ReleaseHoldCommand, ReleaseHoldResponse>
{
    public async Task<ReleaseHoldResponse> Handle(ReleaseHoldCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var account = await db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == command.UserId, ct)
            ?? throw new NotFoundException("credit.account_not_found", "Credit account not found.");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM credit_accounts WHERE id = {account.Id} FOR NO KEY UPDATE", ct);

        var hold = await db.CreditHolds
            .FirstOrDefaultAsync(h => h.Id == command.ReservationId && h.AccountId == account.Id, ct)
            ?? throw new NotFoundException("credit.reservation_not_found", "Reservation not found.");

        if (hold.Status != HoldStatus.Active)
        {
            return new ReleaseHoldResponse(account.Id, account.Available);
        }

        db.CreditEntries.Add(new CreditEntry
        {
            AccountId = account.Id,
            Direction = CreditDirection.Credit,
            Amount = hold.Amount,
            TransactionId = hold.Id,
            Type = CreditEntryType.Release,
            BucketId = null,
            IdempotencyKey = $"release:{hold.Id}",
            CreatedAt = now,
        });

        hold.Status = HoldStatus.Released;
        hold.ResolvedAt = now;

        var activeHolds = await db.CreditHolds
            .Where(h => h.AccountId == account.Id && h.Status == HoldStatus.Active && h.ExpiresAt > now)
            .SumAsync(h => (long?)h.Amount, ct) ?? 0L;
        account.Pending = activeHolds;
        account.Available = account.Posted - activeHolds;

        await db.SaveChangesAsync(ct);

        return new ReleaseHoldResponse(account.Id, account.Available);
    }
}
