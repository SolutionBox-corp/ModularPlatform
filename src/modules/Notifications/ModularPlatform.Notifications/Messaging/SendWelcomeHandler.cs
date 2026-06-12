using Microsoft.Extensions.Logging;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Contracts;
using ModularPlatform.Notifications.Features.Notifications.SendNotification;

namespace ModularPlatform.Notifications.Messaging;

/// <summary>
/// Reacts to Identity's <see cref="UserRegisteredIntegrationEvent"/> by sending a "welcome" notification
/// (email + in-app). Wolverine auto-discovers this <c>Handle</c>; the inbox dedups. Reuses the
/// <see cref="SendNotificationCommand"/> slice (one source of truth for delivery) rather than re-implementing.
/// A missing "welcome" template is NON-FATAL — a fresh deploy without the seed must not poison the inbox.
/// </summary>
public sealed class SendWelcomeHandler(ILogger<SendWelcomeHandler> logger)
{
    public async Task Handle(UserRegisteredIntegrationEvent message, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["locale"] = "en",
            ["email"] = message.Email,
            ["displayName"] = message.DisplayName ?? message.Email,
        };

        try
        {
            await dispatcher.Send(
                new SendNotificationCommand(message.UserId, "welcome", ["email", "inapp"], data,
                    IdempotencyKey: $"welcome:{message.UserId:N}"), ct);
        }
        catch (NotFoundException)
        {
            // No "welcome" template configured for this deployment — skip rather than dead-letter every signup.
            logger.LogWarning("Welcome notification skipped for {UserId}: no 'welcome' template.", message.UserId);
        }
    }
}
