using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Contracts;
using ModularPlatform.Notifications.Features.Notifications.SendNotification;

namespace ModularPlatform.Notifications.Messaging;

/// <summary>
/// Reacts to Identity's <see cref="UserRegisteredIntegrationEvent"/> by sending a "welcome" notification
/// (email + in-app). Wolverine auto-discovers this <c>Handle</c>; the inbox dedups. Reuses the
/// <see cref="SendNotificationCommand"/> slice (one source of truth for delivery) rather than re-implementing.
/// Locale defaults to "en"; the recipient email is carried so the email channel can address it.
/// </summary>
internal sealed class UserRegisteredHandler
{
    public async Task Handle(UserRegisteredIntegrationEvent message, IDispatcher dispatcher, CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["locale"] = "en",
            ["email"] = message.Email,
            ["displayName"] = message.DisplayName ?? message.Email,
        };

        await dispatcher.Send(
            new SendNotificationCommand(message.UserId, "welcome", ["email", "inapp"], data), ct);
    }
}
