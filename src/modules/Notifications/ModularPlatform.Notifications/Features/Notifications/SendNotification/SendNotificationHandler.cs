using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Contracts;
using ModularPlatform.Notifications.Entities;
using ModularPlatform.Notifications.Persistence;
using ModularPlatform.Notifications.Rendering;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Notifications.Features.Notifications.SendNotification;

/// <summary>
/// Persists ONE in-app feed row and hands off durable per-channel delivery. The row + every channel
/// message commit in ONE transaction via the outbox; the Worker performs the actual SMTP/push send.
/// In-app delivery additionally fires a realtime push through the <see cref="IRealtimePublisher"/> port.
/// Locale resolves from data["locale"] (default "en"); the template falls back to "en" if the locale is missing.
/// </summary>
internal sealed class SendNotificationHandler(
    IDbContextOutbox<NotificationsDbContext> outbox,
    IRealtimePublisher realtime,
    IClock clock)
    : ICommandHandler<SendNotificationCommand, Unit>
{
    public async Task<Unit> Handle(SendNotificationCommand command, CancellationToken ct)
    {
        var db = outbox.DbContext;

        var locale = command.Data.TryGetValue("locale", out var l) && !string.IsNullOrWhiteSpace(l) ? l : "en";

        var template = await db.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Key == command.TemplateKey && t.Locale == locale, ct)
            ?? await db.NotificationTemplates
                .FirstOrDefaultAsync(t => t.Key == command.TemplateKey && t.Locale == "en", ct)
            ?? throw new NotFoundException("notification.template_not_found", "Notification template not found.");

        var title = TemplateRenderer.Render(template.Subject, command.Data);
        var body = TemplateRenderer.Render(template.Body, command.Data);

        var notification = new Notification
        {
            UserId = command.UserId,
            TemplateKey = command.TemplateKey,
            Channel = "inapp",
            Title = title,
            Body = body,
            ReadAt = null,
        };
        db.Notifications.Add(notification);

        foreach (var channel in command.Channels.Distinct())
        {
            switch (channel)
            {
                case "email":
                    await outbox.PublishAsync(new EmailDeliveryRequested(
                        EventId: Guid.CreateVersion7(),
                        OccurredAt: clock.UtcNow,
                        NotificationId: notification.Id,
                        UserId: command.UserId,
                        ToAddress: command.Data.TryGetValue("email", out var to) ? to : string.Empty,
                        Subject: title,
                        Body: body));
                    break;

                case "push":
                    await outbox.PublishAsync(new PushDeliveryRequested(
                        EventId: Guid.CreateVersion7(),
                        OccurredAt: clock.UtcNow,
                        NotificationId: notification.Id,
                        UserId: command.UserId,
                        Title: title,
                        Body: body));
                    break;

                case "inapp":
                    await realtime.PublishToUserAsync(
                        command.UserId,
                        "notification",
                        new { notification.Id, notification.Title, notification.Body, notification.TemplateKey },
                        ct);
                    break;
            }
        }

        await outbox.SaveChangesAndFlushMessagesAsync();

        return Unit.Value;
    }
}
