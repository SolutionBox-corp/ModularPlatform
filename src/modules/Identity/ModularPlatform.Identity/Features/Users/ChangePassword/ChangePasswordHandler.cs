using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Users.ChangePassword;

/// <summary>
/// Verifies the current password, sets the new one (Argon2id), and revokes ALL of the user's active refresh tokens.
/// Revocation is on TRACKED entities so the <c>AuditInterceptor</c> records the family kill (security-relevant) and
/// xmin guards against a concurrent token mutation. A wrong current password is an UNAUTHORIZED 401 with a generic
/// code (no oracle on which factor was wrong).
/// </summary>
internal sealed class ChangePasswordHandler(IdentityDbContext db, IPasswordHasher passwordHasher, IClock clock)
    : ICommandHandler<ChangePasswordCommand>
{
    public async Task<Unit> Handle(ChangePasswordCommand command, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == command.UserId, ct)
            ?? throw new NotFoundException("user.not_found", "User not found.");

        if (!passwordHasher.Verify(user.PasswordHash, command.CurrentPassword))
        {
            throw new UnauthorizedException("user.current_password_invalid", "The current password is incorrect.");
        }

        // Reject a no-op rotation (new == current) so "change" always means a real change.
        if (passwordHasher.Verify(user.PasswordHash, command.NewPassword))
        {
            throw new BusinessRuleException("user.password_unchanged", "The new password must differ from the current one.");
        }

        user.PasswordHash = passwordHasher.Hash(command.NewPassword);

        // Revoke every active session for the user — a rotated credential invalidates all prior sessions.
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
