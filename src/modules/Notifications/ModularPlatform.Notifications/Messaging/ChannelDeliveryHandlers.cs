using ModularPlatform.Notifications.Channels;
using ModularPlatform.Notifications.Contracts;

namespace ModularPlatform.Notifications.Messaging;

/// <summary>
/// Worker-side durable delivery. Wolverine auto-discovers these <c>Handle</c> methods; the inbox dedups
/// each message. The actual SMTP / push send happens HERE (in the Worker), never inline in the API request.
/// </summary>
public sealed class EmailDeliveryHandler
{
    public Task Handle(EmailDeliveryRequested message, IEmailSender emailSender, CancellationToken ct)
        => string.IsNullOrWhiteSpace(message.ToAddress)
            ? Task.CompletedTask
            : emailSender.SendAsync(message.ToAddress, message.Subject, message.Body, ct);
}

public sealed class PushDeliveryHandler
{
    public Task Handle(PushDeliveryRequested message, IPushSender pushSender, CancellationToken ct)
        => pushSender.SendAsync(message.UserId, message.Title, message.Body, ct);
}
