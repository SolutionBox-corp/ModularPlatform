using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.UnlockUser;

internal sealed class UnlockUserHandler(IdentityDbContext db) : ICommandHandler<UnlockUserCommand, Unit>
{
    public async Task<Unit> Handle(UnlockUserCommand command, CancellationToken ct)
    {
        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == command.UserId && u.DeletedAt == null, ct)
            ?? throw new NotFoundException("user.not_found", "User not found.");

        user.FailedAccessCount = 0;
        user.LockoutEndUtc = null;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
