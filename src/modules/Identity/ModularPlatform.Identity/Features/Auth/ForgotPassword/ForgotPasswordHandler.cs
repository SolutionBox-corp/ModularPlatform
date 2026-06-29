using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Authorization;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.Identity.Security;
using ModularPlatform.Notifications.Contracts;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Identity.Features.Auth.ForgotPassword;

internal sealed class ForgotPasswordHandler(
    IDbContextOutbox<IdentityDbContext> outbox,
    IBlindIndexHasher blindIndex,
    ITokenIssuer tokenIssuer,
    IClock clock,
    IOptions<PasswordResetOptions> options)
    : ICommandHandler<ForgotPasswordCommand, ForgotPasswordResponse>
{
    public async Task<ForgotPasswordResponse> Handle(ForgotPasswordCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;
        var now = clock.UtcNow;
        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var emailHash = blindIndex.Hash(normalizedEmail);

        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.EmailHash == emailHash, ct);

        if (user is null || user.DeletedAt is not null || string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            return new ForgotPasswordResponse(true);
        }

        var outstanding = await db.PasswordResetTokens
            .Where(t => t.UserId == user.Id && t.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var token in outstanding)
        {
            token.ConsumedAt = now;
        }

        var resetToken = tokenIssuer.CreateRefreshToken();
        var tokenRow = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = resetToken.Hash,
            ExpiresAt = now.AddMinutes(Math.Max(1, options.Value.TokenLifetimeMinutes)),
        };
        db.PasswordResetTokens.Add(tokenRow);

        var resetLink = BuildResetLink(options.Value.ResetUrl, resetToken.Raw);
        await outbox.PublishAsync(new EmailDeliveryRequested(
            EventId: Guid.CreateVersion7(),
            OccurredAt: now,
            NotificationId: tokenRow.Id,
            UserId: user.Id,
            ToAddress: user.Email,
            Subject: "Reset your ModularPlatform password",
            Body: $"Open this link to set a new password: {resetLink}\n\nIf you did not request this, ignore this email."));

        await outbox.SaveChangesAndFlushMessagesAsync();
        return new ForgotPasswordResponse(true);
    }

    private static string BuildResetLink(string resetUrl, string rawToken)
    {
        var separator = resetUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{resetUrl}{separator}token={Uri.EscapeDataString(rawToken)}";
    }
}
