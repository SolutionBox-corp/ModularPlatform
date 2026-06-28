using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Auth.VerifyEmail;

internal sealed class VerifyEmailHandler(
    IdentityDbContext db,
    ITokenIssuer tokenIssuer,
    IClock clock)
    : ICommandHandler<VerifyEmailCommand>
{
    public async Task<Unit> Handle(VerifyEmailCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var tokenHash = tokenIssuer.HashRefreshToken(command.Token.Trim());
        var verificationToken = await db.EmailVerificationTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);
        if (verificationToken is null || !verificationToken.IsUsable(now))
        {
            throw InvalidToken();
        }

        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == verificationToken.UserId, ct);
        if (user is null || user.DeletedAt is not null || user.EmailConfirmed)
        {
            throw InvalidToken();
        }

        user.EmailConfirmed = true;
        user.EmailConfirmedAt = now;

        var outstandingTokens = await db.EmailVerificationTokens
            .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var token in outstandingTokens)
        {
            token.ConsumedAt = now;
        }

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static BusinessRuleException InvalidToken()
        => new("auth.email_verification_invalid", "This email verification link is invalid or expired.");
}
