namespace ModularPlatform.Notifications.Channels;

/// <summary>
/// Sends a push notification for the push channel. Called from the Worker. Production deployments can route through
/// <see cref="WebhookPushSender"/>; local/dev deployments without a provider use <see cref="NoOpPushSender"/>.
/// </summary>
public interface IPushSender
{
    Task SendAsync(Guid userId, string title, string body, CancellationToken ct);
}
