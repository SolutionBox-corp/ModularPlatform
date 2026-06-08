using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Web;
using RefreshTokenEntity = ModularPlatform.Identity.Entities.RefreshToken;

namespace ModularPlatform.Identity.Features.Auth.Login;

/// <summary>
/// Verifies credentials and issues a short-lived access token + a refresh token that starts a new
/// rotation family (stored hashed). Pure DB write (no integration event) → injects the scoped DbContext
/// directly rather than the outbox.
/// </summary>
internal sealed class LoginHandler(
    IdentityDbContext db,
    IPasswordHasher passwordHasher,
    ITokenIssuer tokenIssuer,
    IClock clock,
    IOptions<JwtOptions> jwtOptions)
    : ICommandHandler<LoginCommand, AuthTokensResponse>
{
    public async Task<AuthTokensResponse> Handle(LoginCommand command, CancellationToken ct)
    {
        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        if (user is null || !passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            throw new UnauthorizedException("auth.invalid_credentials", "Invalid email or password.");
        }

        var access = tokenIssuer.IssueAccessToken(user.Id, tenantId: null, user.Email);
        var refresh = tokenIssuer.CreateRefreshToken();

        db.RefreshTokens.Add(new RefreshTokenEntity
        {
            UserId = user.Id,
            FamilyId = Guid.CreateVersion7(),
            TokenHash = refresh.Hash,
            CreatedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays),
        });

        await db.SaveChangesAsync(ct);

        return new AuthTokensResponse(access.Value, access.ExpiresAt, refresh.Raw);
    }
}
