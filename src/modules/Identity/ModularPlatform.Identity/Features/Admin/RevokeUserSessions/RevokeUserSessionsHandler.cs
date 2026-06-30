using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.RevokeUserSessions;

internal sealed class RevokeUserSessionsHandler(IdentityDbContext db, IClock clock)
    : ICommandHandler<RevokeUserSessionsCommand, Unit>
{
    public async Task<Unit> Handle(RevokeUserSessionsCommand command, CancellationToken ct)
    {
        if (!await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == command.UserId && u.DeletedAt == null, ct))
        {
            throw new NotFoundException("user.not_found", "User not found.");
        }

        var now = clock.UtcNow;
        var activeTokens = await db.RefreshTokens
            .Where(t => t.UserId == command.UserId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in activeTokens)
        {
            token.RevokedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
