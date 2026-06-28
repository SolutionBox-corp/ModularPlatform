using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Auth.ResetPassword;

internal sealed class ResetPasswordHandler(
    IdentityDbContext db,
    ITokenIssuer tokenIssuer,
    IPasswordHasher passwordHasher,
    IClock clock)
    : ICommandHandler<ResetPasswordCommand>
{
    public async Task<Unit> Handle(ResetPasswordCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tokenHash = tokenIssuer.HashRefreshToken(command.Token.Trim());
        var resetToken = await db.PasswordResetTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (resetToken is null || !resetToken.IsUsable(now))
        {
            throw InvalidToken();
        }

        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == resetToken.UserId, ct);
        if (user is null || user.DeletedAt is not null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            throw InvalidToken();
        }

        if (passwordHasher.Verify(user.PasswordHash, command.NewPassword))
        {
            throw new BusinessRuleException("user.password_unchanged", "The new password must differ from the current one.");
        }

        user.PasswordHash = passwordHasher.Hash(command.NewPassword);
        resetToken.ConsumedAt = now;

        var outstandingResetTokens = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var token in outstandingResetTokens)
        {
            token.ConsumedAt = now;
        }

        var activeRefreshTokens = await db.RefreshTokens
            .Where(t => t.UserId == user.Id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in activeRefreshTokens)
        {
            token.RevokedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static BusinessRuleException InvalidToken()
        => new("auth.password_reset_invalid", "This password reset link is invalid or expired.");
}
