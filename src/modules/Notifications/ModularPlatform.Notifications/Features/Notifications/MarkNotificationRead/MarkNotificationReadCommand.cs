using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Features.Notifications.MarkNotificationRead;

public sealed record MarkNotificationReadCommand(Guid UserId, Guid NotificationId) : ICommand<Unit>;

public sealed record MarkNotificationReadRequest(Guid NotificationId);
