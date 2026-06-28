using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Authorization;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Notifications.Contracts;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Identity.Features.Users;

internal static class EmailVerificationIssuer
{
    public static async Task IssueAsync(
        IdentityDbContext db,
        IDbContextOutbox<IdentityDbContext> outbox,
        ITokenIssuer tokenIssuer,
        IClock clock,
        EmailVerificationOptions options,
        User user,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var outstanding = await db.EmailVerificationTokens
            .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var token in outstanding)
        {
            token.ConsumedAt = now;
        }

        var verification = tokenIssuer.CreateRefreshToken();
        var tokenRow = new EmailVerificationToken
        {
            UserId = user.Id,
            TokenHash = verification.Hash,
            ExpiresAt = now.AddMinutes(Math.Max(1, options.TokenLifetimeMinutes)),
        };
        db.EmailVerificationTokens.Add(tokenRow);

        var verifyLink = BuildVerifyLink(options.VerifyUrl, verification.Raw);
        await outbox.PublishAsync(new EmailDeliveryRequested(
            EventId: Guid.CreateVersion7(),
            OccurredAt: clock.UtcNow,
            NotificationId: tokenRow.Id,
            UserId: user.Id,
            ToAddress: user.Email,
            Subject: "Verify your ModularPlatform email",
            Body: $"Open this link to verify your email address: {verifyLink}\n\nIf you did not create this account, ignore this email."));
    }

    private static string BuildVerifyLink(string verifyUrl, string rawToken)
    {
        var separator = verifyUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{verifyUrl}{separator}token={Uri.EscapeDataString(rawToken)}";
    }
}
