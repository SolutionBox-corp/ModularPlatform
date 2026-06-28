using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Notifications.Features.Notifications.GetUnreadCount;

/// <summary>
/// Read slice: counts the caller's unread notifications (ReadAt == null). No-tracking read factory.
/// Mirrors GetMyNotifications for the read-context + ownership pattern.
/// </summary>
internal sealed class GetUnreadCountHandler(IReadDbContextFactory<NotificationsDbContext> readFactory)
    : IQueryHandler<GetUnreadCountQuery, UnreadCountResponse>
{
    public async Task<UnreadCountResponse> Handle(GetUnreadCountQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var count = await db.Notifications
            .Where(n => n.UserId == query.UserId && n.ReadAt == null)
            .LongCountAsync(ct);

        return new UnreadCountResponse(count);
    }
}
