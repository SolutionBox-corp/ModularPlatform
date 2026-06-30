using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Authorization;
using ModularPlatform.Identity.Entities;
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
    IBlindIndexHasher blindIndex,
    ITokenIssuer tokenIssuer,
    IClock clock,
    IOptions<JwtOptions> jwtOptions,
    IOptions<IdentityAuthOptions> authOptions)
    : ICommandHandler<LoginCommand, AuthTokensResponse>
{
    private const int MaxFailedAccessAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    // A valid Argon2 hash (platform parameters), computed once on first use, so an unknown-email login spends the
    // same verification time as a real one. The plaintext is irrelevant — only the verification cost matters.
    // Double-checked locking: a bare `??=` on a static field can let concurrent first callers each compute a hash
    // (wasted Argon2 cost) and is not guaranteed atomic; the hasher is stateless so locking it is safe.
    private static readonly object DummyHashLock = new();
    private static string? _dummyPasswordHash;
    private string DummyPasswordHash()
    {
        if (_dummyPasswordHash is { } cached)
        {
            return cached;
        }

        lock (DummyHashLock)
        {
            return _dummyPasswordHash ??= passwordHasher.Hash("d6f1c0a2-login-timing-equalization-seed");
        }
    }

    public async Task<AuthTokensResponse> Handle(LoginCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        // Email at rest is ciphertext — the pre-auth lookup goes through the keyed blind index.
        var emailHash = blindIndex.Hash(command.Email.Trim().ToUpperInvariant());
        // Authentication is cross-tenant — look the user up globally (the tenant is unknown until logged in).
        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.EmailHash == emailHash, ct);

        // No user enumeration: ALWAYS run a password verification — against the real hash, or a fixed dummy hash for
        // an unknown/erased account — so the response time is identical whether or not the email exists. Argon2 is
        // deliberately slow; skipping it for an unknown email would leak account existence via response latency.
        // An account with no real hash (unknown OR GDPR-erased -> blanked PasswordHash) can NEVER authenticate, even
        // if the password happens to match the dummy hash, because passwordValid is gated on hasRealHash.
        var hasRealHash = user?.PasswordHash is { Length: > 0 };
        var storedHash = hasRealHash ? user!.PasswordHash : DummyPasswordHash();
        var passwordValid = passwordHasher.Verify(storedHash, command.Password) && hasRealHash;

        // Unknown user: nothing to mutate; respond with the generic credential error (no user enumeration).
        if (user is null)
        {
            throw new UnauthorizedException("auth.invalid_credentials", "Invalid email or password.");
        }

        // Reject even CORRECT credentials while the account is locked out — but reveal the LOCKOUT only to a caller
        // who proved the password. Otherwise "locked" (after 5 wrong guesses) vs "unknown email" is a user-enumeration
        // oracle: a wrong password during lockout returns the SAME generic error as an unknown email.
        if (user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > now)
        {
            throw passwordValid
                ? new UnauthorizedException("auth.locked_out", "This account is temporarily locked. Try again later.")
                : new UnauthorizedException("auth.invalid_credentials", "Invalid email or password.");
        }

        if (!passwordValid)
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

        // Correct credentials, but a deactivated / GDPR-erased account (DeletedAt stamped) must NOT authenticate —
        // mirrors the refresh-token guard. Same generic error (no enumeration). This also stops a soft-deleted admin
        // email from being re-granted admin by EnsureConfiguredAdminAsync below.
        if (user.DeletedAt is not null)
        {
            throw new UnauthorizedException("auth.invalid_credentials", "Invalid email or password.");
        }

        // Successful login: clear any accumulated failure state.
        if (user.FailedAccessCount != 0 || user.LockoutEndUtc is not null)
        {
            user.FailedAccessCount = 0;
            user.LockoutEndUtc = null;
        }

        // Bootstrap the first admin by config: a user whose email is in Identity:Auth:AdminEmails gets the admin
        // role on login (works even if they registered after startup, which the startup seeder can't catch).
        await EnsureConfiguredAdminAsync(user, ct);

        var tenantId = db.Entry(user).Property<Guid?>("TenantId").CurrentValue;
        var (roles, permissions) = await UserAuthorizationQuery.LoadAsync(db, user.Id, ct);
        var access = tokenIssuer.IssueAccessToken(user.Id, tenantId, user.Email, roles, permissions);
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

    /// <summary>
    /// Idempotently grants the system admin role to a user whose email is configured in
    /// <c>Identity:Auth:AdminEmails</c>. Persisted before the roles are loaded so the issued token already
    /// carries admin claims on the very first admin login. No-op when no admin emails are configured.
    /// </summary>
    private async Task EnsureConfiguredAdminAsync(User user, CancellationToken ct)
    {
        if (user.DeletedAt is not null)
        {
            return;
        }

        // user.Email is decrypted in memory (the model-level converter), so the comparison stays plaintext.
        var userNormalizedEmail = user.Email.Trim().ToUpperInvariant();
        var adminEmails = authOptions.Value.AdminEmails;
        if (adminEmails.Length == 0
            || !adminEmails.Any(e => e.Trim().ToUpperInvariant() == userNormalizedEmail))
        {
            return;
        }

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == SystemRoles.Admin, ct);
        if (adminRole is null)
        {
            return; // the seeder hasn't created the role yet; the next login will catch it.
        }

        if (!await db.UserRoles.AnyAsync(ur => ur.UserId == user.Id && ur.RoleId == adminRole.Id, ct))
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Two concurrent first-logins of the configured admin raced past the pre-check — the
                // UNIQUE(UserId, RoleId) index is the final guard. The role IS assigned; not a 500 (Law 2 idiom).
            }
        }
    }
}
