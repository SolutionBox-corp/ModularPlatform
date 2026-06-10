using Microsoft.EntityFrameworkCore;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.EnsureCreditAccount;

/// <summary>Idempotently provisions a zero-balance credit account for a user (no-op if one exists).</summary>
internal sealed record EnsureCreditAccountCommand(Guid UserId) : ICommand;

internal sealed class EnsureCreditAccountHandler(BillingDbContext db)
    : ICommandHandler<EnsureCreditAccountCommand, Unit>
{
    public async Task<Unit> Handle(EnsureCreditAccountCommand command, CancellationToken ct)
    {
        if (!await db.CreditAccounts.AnyAsync(a => a.UserId == command.UserId, ct))
        {
            db.CreditAccounts.Add(new CreditAccount { UserId = command.UserId, Posted = 0, Pending = 0, Available = 0 });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex is not DbUpdateConcurrencyException)
            {
                // Lost the UNIQUE(UserId) creation race (EV-5) — exactly one account stands; idempotent no-op.
            }
        }

        return Unit.Value;
    }
}
