namespace ModularPlatform.Notifications.Features.Preferences;

internal static class NotificationChannels
{
    public static readonly string[] Configurable = ["email", "push"];
    public static readonly string[] All = ["inapp", "email", "push"];

    public static bool IsConfigurable(string channel) => Configurable.Contains(channel);
}
