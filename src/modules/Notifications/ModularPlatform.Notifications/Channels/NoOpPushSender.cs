namespace ModularPlatform.Notifications.Channels;

/// <summary>
/// Stub push sender. Real FCM/Expo delivery lands later; until then push delivery is a no-op so the
/// durable channel pipeline is exercised end-to-end without an external provider.
/// </summary>
internal sealed class NoOpPushSender : IPushSender
{
    public Task SendAsync(Guid userId, string title, string body, CancellationToken ct) => Task.CompletedTask;
}
