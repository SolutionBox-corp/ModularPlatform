using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.ReserveCredits;

/// <summary>
/// PESSIMISTIC debit, done the EF-native way: a single conditional <c>ExecuteUpdate</c> with a
/// <c>Available &gt;= amount</c> guard. Postgres locks the row for that UPDATE and the guard is evaluated
/// atomically, so concurrent reservations serialize at the database with no double-spend and no retry storm —
/// no raw SQL, no <c>FOR UPDATE</c>. The hold + ledger entry are written in the same transaction so the
/// reserved amount always has a matching hold. <c>available</c>/<c>pending</c> are authoritative stored columns.
/// </summary>
internal sealed class ReserveCreditsHandler(BillingDbContext db, IClock clock)
    : ICommandHandler<ReserveCreditsCommand, ReserveCreditsResponse>
{
    private const int DefaultHoldMinutes = 15;

    public async Task<ReserveCreditsResponse> Handle(ReserveCreditsCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var accountId = await db.CreditAccounts
            .Where(a => a.UserId == command.UserId)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);
        if (accountId == Guid.Empty)
        {
            throw new NotFoundException("credit.account_not_found", "Credit account not found.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Atomic check-and-debit: the row is locked by the UPDATE and only decremented when sufficient.
        var debited = await db.CreditAccounts
            .Where(a => a.Id == accountId && a.Available >= command.Amount)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Available, a => a.Available - command.Amount)
                .SetProperty(a => a.Pending, a => a.Pending + command.Amount), ct);

        if (debited == 0)
        {
            throw new BusinessRuleException(
                "credit.insufficient_balance", "Insufficient available credit for this reservation.");
        }

        var hold = new CreditHold
        {
            AccountId = accountId,
            Amount = command.Amount,
            Status = HoldStatus.Active,
            ExpiresAt = now.AddMinutes(command.HoldMinutes ?? DefaultHoldMinutes),
            CreatedAt = now,
        };
        db.CreditHolds.Add(hold);

        db.CreditEntries.Add(new CreditEntry
        {
            AccountId = accountId,
            Direction = CreditDirection.Debit,
            Amount = command.Amount,
            TransactionId = hold.Id,
            Type = CreditEntryType.Reservation,
            BucketId = null,
            IdempotencyKey = $"reserve:{hold.Id}",
            CreatedAt = now,
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var available = await db.CreditAccounts.Where(a => a.Id == accountId).Select(a => a.Available).FirstAsync(ct);
        return new ReserveCreditsResponse(hold.Id, available);
    }
}
