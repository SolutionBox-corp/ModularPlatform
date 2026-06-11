using Microsoft.EntityFrameworkCore;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Credits.EnsureCreditAccount;

/// <summary>Idempotently provisions a zero-balance credit account for a user (no-op if one exists). <c>TenantId</c> is stamped explicitly — the Worker SYSTEM context does not auto-stamp it.</summary>
internal sealed record EnsureCreditAccountCommand(Guid UserId, Guid? TenantId = null) : ICommand;

internal sealed class EnsureCreditAccountHandler(BillingDbContext db)
    : ICommandHandler<EnsureCreditAccountCommand, Unit>
{
    public async Task<Unit> Handle(EnsureCreditAccountCommand command, CancellationToken ct)
    {
        if (!await db.CreditAccounts.AnyAsync(a => a.UserId == command.UserId, ct))
        {
            db.CreditAccounts.Add(new CreditAccount
            {
                UserId = command.UserId,
                TenantId = command.TenantId, // explicit: SYSTEM Worker context does not auto-stamp
                Posted = 0,
                Pending = 0,
                Available = 0,
            });
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
