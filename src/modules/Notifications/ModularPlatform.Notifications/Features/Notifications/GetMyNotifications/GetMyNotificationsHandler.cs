using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Notifications.Features.Notifications.GetMyNotifications;

/// <summary>
/// Read slice for the in-app feed. No-tracking read factory; projects straight to the response DTO.
/// Newest first; optionally filters to unread (ReadAt == null).
/// </summary>
internal sealed class GetMyNotificationsHandler(IReadDbContextFactory<NotificationsDbContext> readFactory)
    : IQueryHandler<GetMyNotificationsQuery, PagedResponse<NotificationItem>>
{
    public async Task<PagedResponse<NotificationItem>> Handle(GetMyNotificationsQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var feed = db.Notifications.Where(n => n.UserId == query.UserId);

        if (query.UnreadOnly)
        {
            feed = feed.Where(n => n.ReadAt == null);
        }

        return await feed
            .OrderByDescending(n => n.CreatedAt)
            .ThenByDescending(n => n.Id)
            .Select(n => new NotificationItem(n.Id, n.TemplateKey, n.Title, n.Body, n.ReadAt, n.CreatedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
