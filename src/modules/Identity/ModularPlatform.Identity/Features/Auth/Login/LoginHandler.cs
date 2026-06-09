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
    private const int MaxFailedAccessAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public async Task<AuthTokensResponse> Handle(LoginCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        // Authentication is cross-tenant — look the user up globally (the tenant is unknown until logged in).
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, ct);

        // Unknown user: nothing to mutate; respond with the generic credential error (no user enumeration).
        if (user is null)
        {
            throw new UnauthorizedException("auth.invalid_credentials", "Invalid email or password.");
        }

        // Reject even CORRECT credentials while the account is locked out.
        if (user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > now)
        {
            throw new UnauthorizedException("auth.locked_out",
                "This account is temporarily locked. Try again later.");
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            // Wrong password: count the failure; lock out once the threshold is crossed.
            user.FailedAccessCount += 1;
            if (user.FailedAccessCount >= MaxFailedAccessAttempts)
            {
                user.LockoutEndUtc = now.Add(LockoutDuration);
                user.FailedAccessCount = 0;
            }

            await db.SaveChangesAsync(ct);

            throw new UnauthorizedException("auth.invalid_credentials", "Invalid email or password.");
        }

        // Successful login: clear any accumulated failure state.
        if (user.FailedAccessCount != 0 || user.LockoutEndUtc is not null)
        {
            user.FailedAccessCount = 0;
            user.LockoutEndUtc = null;
        }

        var tenantId = db.Entry(user).Property<Guid?>("TenantId").CurrentValue;
        var access = tokenIssuer.IssueAccessToken(user.Id, tenantId, user.Email);
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
