using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Features.Preferences;
using ModularPlatform.Notifications.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Notifications.Features.Preferences.GetMyNotificationPreferences;

internal sealed class GetMyNotificationPreferencesHandler(IReadDbContextFactory<NotificationsDbContext> readFactory)
    : IQueryHandler<GetMyNotificationPreferencesQuery, GetMyNotificationPreferencesResponse>
{
    public async Task<GetMyNotificationPreferencesResponse> Handle(GetMyNotificationPreferencesQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var stored = await db.NotificationPreferences
            .Where(p => p.UserId == query.UserId)
            .Select(p => new { p.Channel, p.Enabled })
            .ToListAsync(ct);

        var byChannel = stored.ToDictionary(p => p.Channel, p => p.Enabled, StringComparer.Ordinal);
        var items = NotificationChannels.All
            .Select(channel => new NotificationPreferenceItem(
                channel,
                byChannel.TryGetValue(channel, out var enabled) ? enabled : true,
                NotificationChannels.IsConfigurable(channel)))
            .ToArray();

        return new GetMyNotificationPreferencesResponse(items);
    }
}
