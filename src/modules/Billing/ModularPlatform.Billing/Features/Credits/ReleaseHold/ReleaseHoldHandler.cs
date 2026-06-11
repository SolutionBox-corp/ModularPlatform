using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ReleaseHold;

/// <summary>
/// Releases an active reservation, restoring availability. EF-native: the hold and account are tracked, so a
/// concurrent double-release conflicts on the xmin token and is retried (then sees the hold already resolved and
/// returns idempotently). Keeps the invariant <c>available = posted - pending</c>. No raw SQL.
/// </summary>
internal sealed class ReleaseHoldHandler(BillingDbContext db, IClock clock)
    : ICommandHandler<ReleaseHoldCommand, ReleaseHoldResponse>
{
    public async Task<ReleaseHoldResponse> Handle(ReleaseHoldCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var account = await db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == command.UserId, ct)
            ?? throw new NotFoundException("credit.account_not_found", "Credit account not found.");

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

        // The reservation is cancelled: availability is restored and the pending hold is removed.
        account.Available += hold.Amount;
        account.Pending -= hold.Amount;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
        {
            // A concurrent release already ran (UNIQUE release:{holdId}). Idempotent: report current state.
            if (await db.CreditEntries.AsNoTracking().AnyAsync(
                    e => e.AccountId == account.Id && e.IdempotencyKey == $"release:{hold.Id}", ct))
            {
                var available = await db.CreditAccounts.AsNoTracking()
                    .Where(a => a.Id == account.Id).Select(a => a.Available).FirstAsync(ct);
                return new ReleaseHoldResponse(account.Id, available);
            }

            throw;
        }

        return new ReleaseHoldResponse(account.Id, account.Available);
    }
}
