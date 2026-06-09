using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Web;
using TokenEntity = ModularPlatform.Identity.Entities.RefreshToken;

namespace ModularPlatform.Identity.Features.Auth.RefreshToken;

/// <summary>
/// Refresh-token ROTATION with REUSE DETECTION (the security-critical slice):
/// <list type="bullet">
/// <item>unknown token hash -&gt; invalid;</item>
/// <item>already-consumed token replayed -&gt; the whole family is compromised: revoke EVERY token in the
/// family and reject (forces re-login);</item>
/// <item>active token -&gt; consume it, issue a new token in the same family, link via ReplacedByTokenId.</item>
/// </list>
/// </summary>
internal sealed class RefreshTokenHandler(
    IdentityDbContext db,
    ITokenIssuer tokenIssuer,
    IClock clock,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<RefreshTokenCommand, AuthTokensResponse>
{
    public async Task<AuthTokensResponse> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = tokenIssuer.HashRefreshToken(command.RefreshToken);

        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null)
        {
            throw new UnauthorizedException("auth.refresh_token_invalid", "The refresh token is invalid.");
        }

        if (token.ConsumedAt is not null)
        {
            // Reuse of a consumed token: the family is compromised. Revoke all of it.
            // SECURITY: load the family TRACKED and SaveChanges so the AuditInterceptor records this
            // revocation (a security event) and xmin concurrency is honored — never a set-based
            // ExecuteUpdate, which bypasses both. Reuse-detection semantics are unchanged.
            var family = await db.RefreshTokens
                .Where(t => t.FamilyId == token.FamilyId && t.RevokedAt == null)
                .ToListAsync(ct);

            foreach (var familyToken in family)
            {
                familyToken.RevokedAt = now;
            }

            await db.SaveChangesAsync(ct);

            throw new UnauthorizedException("auth.refresh_token_reused",
                "This session has been revoked for security reasons.");
        }

        if (!token.IsActive(now))
        {
            throw new UnauthorizedException("auth.refresh_token_invalid", "The refresh token has expired or was revoked.");
        }

        var newRefresh = tokenIssuer.CreateRefreshToken();
        var replacement = new TokenEntity
        {
            UserId = token.UserId,
            FamilyId = token.FamilyId,
            TokenHash = newRefresh.Hash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(jwtOptions.Value.RefreshTokenDays),
        };
        db.RefreshTokens.Add(replacement);

        token.ConsumedAt = now;
        token.ReplacedByTokenId = replacement.Id;

        var user = await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Id == token.UserId, ct);
        var tenantId = db.Entry(user).Property<Guid?>("TenantId").CurrentValue;
        var (roles, permissions) = await UserAuthorizationQuery.LoadAsync(db, user.Id, ct);
        var access = tokenIssuer.IssueAccessToken(user.Id, tenantId, user.Email, roles, permissions);

        await db.SaveChangesAsync(ct);

        return new AuthTokensResponse(access.Value, access.ExpiresAt, newRefresh.Raw);
    }
}
