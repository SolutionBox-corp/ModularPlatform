using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Features.Preferences.GetMyNotificationPreferences;

public sealed record GetMyNotificationPreferencesQuery(Guid UserId)
    : IQuery<GetMyNotificationPreferencesResponse>;

public sealed record NotificationPreferenceItem(string Channel, bool Enabled, bool Configurable);

public sealed record GetMyNotificationPreferencesResponse(IReadOnlyList<NotificationPreferenceItem> Items);
