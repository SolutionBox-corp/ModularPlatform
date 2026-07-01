using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Features.Preferences.SetNotificationPreference;

public sealed record SetNotificationPreferenceCommand(Guid UserId, string Channel, bool Enabled)
    : ICommand<SetNotificationPreferenceResponse>;

public sealed record SetNotificationPreferenceRequest(bool Enabled);

public sealed record SetNotificationPreferenceResponse(string Channel, bool Enabled);
