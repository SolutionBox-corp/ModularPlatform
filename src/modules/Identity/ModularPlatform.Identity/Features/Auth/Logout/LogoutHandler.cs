using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;

namespace ModularPlatform.Identity.Features.Auth.Logout;

/// <summary>
/// Explicit logout / session-revocation: revokes the WHOLE rotation family of the presented refresh token, so a
/// user (or an admin off-boarding them) has a kill switch for a stolen or stale session. Identity is taken from
/// the access token (never a body id); a token that is unknown or does not belong to the caller is a silent
/// no-op (idempotent, non-enumerating). Family revoke is TRACKED + SaveChanges so the AuditInterceptor records
/// the security event and xmin concurrency holds — mirroring reuse detection.
/// </summary>
internal sealed class LogoutHandler(IdentityDbContext db, ITokenIssuer tokenIssuer, IClock clock)
    : ICommandHandler<LogoutCommand, Unit>
{
    public async Task<Unit> Handle(LogoutCommand command, CancellationToken ct)
    {
        var hash = tokenIssuer.HashRefreshToken(command.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || token.UserId != command.UserId)
        {
            return Unit.Value;
        }

        var now = clock.UtcNow;
        var family = await db.RefreshTokens
            .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var familyToken in family)
        {
            familyToken.RevokedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
