using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Features.Notifications.GetMyNotifications;

public sealed record GetMyNotificationsQuery(Guid UserId, bool UnreadOnly) : IQuery<IReadOnlyList<NotificationItem>>;

public sealed record NotificationItem(
    Guid Id,
    string TemplateKey,
    string Title,
    string Body,
    DateTimeOffset? ReadAt,
    DateTimeOffset CreatedAt);
