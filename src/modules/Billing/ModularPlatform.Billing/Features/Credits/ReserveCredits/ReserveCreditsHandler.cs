using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ReserveCredits;

/// <summary>
/// PESSIMISTIC debit path. Locks the account row (<c>FOR NO KEY UPDATE</c>), then computes
/// <c>available = posted - active(non-expired) holds</c> INSIDE the lock and refuses to let it go negative
/// (invariant <c>available &gt;= 0</c>). Creates a hold with a hard expiry — an expired hold is ignored by the
/// availability query even before the sweep. No integration event, so it injects the scoped DbContext directly.
/// Concurrency on this path is serialized by the lock, NOT optimistic.
/// </summary>
internal sealed class ReserveCreditsHandler(BillingDbContext db, IClock clock)
    : ICommandHandler<ReserveCreditsCommand, ReserveCreditsResponse>
{
    private const int DefaultHoldMinutes = 15;

    public async Task<ReserveCreditsResponse> Handle(ReserveCreditsCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;

        // Explicit transaction so the row lock is HELD until commit (a lock in autocommit releases immediately
        // and serializes nothing). Lock, then reload the account so Posted/xmin are fresh under the lock.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var account = await db.CreditAccounts.FirstOrDefaultAsync(a => a.UserId == command.UserId, ct)
            ?? throw new NotFoundException("credit.account_not_found", "Credit account not found.");

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM credit_accounts WHERE \"Id\" = {account.Id} FOR NO KEY UPDATE", ct);
        await db.Entry(account).ReloadAsync(ct);

        var activeHolds = await db.CreditHolds
            .Where(h => h.AccountId == account.Id && h.Status == HoldStatus.Active && h.ExpiresAt > now)
            .SumAsync(h => (long?)h.Amount, ct) ?? 0L;

        var available = account.Posted - activeHolds;
        if (available < command.Amount)
        {
            throw new BusinessRuleException(
                "credit.insufficient_balance", "Insufficient available credit for this reservation.");
        }

        var hold = new CreditHold
        {
            AccountId = account.Id,
            Amount = command.Amount,
            Status = HoldStatus.Active,
            ExpiresAt = now.AddMinutes(command.HoldMinutes ?? DefaultHoldMinutes),
            CreatedAt = now,
        };
        db.CreditHolds.Add(hold);

        db.CreditEntries.Add(new CreditEntry
        {
            AccountId = account.Id,
            Direction = CreditDirection.Debit,
            Amount = command.Amount,
            TransactionId = hold.Id,
            Type = CreditEntryType.Reservation,
            BucketId = null,
            IdempotencyKey = $"reserve:{hold.Id}",
            CreatedAt = now,
        });

        account.Pending = activeHolds + command.Amount;
        account.Available = available - command.Amount;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ReserveCreditsResponse(hold.Id, account.Available);
    }
}
