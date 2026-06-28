using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Features.Notifications.GetUnreadCount;

public sealed record GetUnreadCountQuery(Guid UserId) : IQuery<UnreadCountResponse>;

public sealed record UnreadCountResponse(long Count);
