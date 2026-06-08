namespace ModularPlatform.Notifications.Channels;

/// <summary>
/// Sends a push notification for the push channel. Called from the Worker. The real FCM/Expo transport
/// is not implemented yet — <see cref="NoOpPushSender"/> is the current stub.
/// </summary>
public interface IPushSender
{
    Task SendAsync(Guid userId, string title, string body, CancellationToken ct);
}
