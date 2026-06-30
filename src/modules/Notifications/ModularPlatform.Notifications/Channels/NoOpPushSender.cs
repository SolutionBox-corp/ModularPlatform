namespace ModularPlatform.Notifications.Channels;

/// <summary>
/// Local/dev fallback when no push webhook is configured. The durable channel pipeline is still exercised end-to-end
/// without requiring an external provider.
/// </summary>
internal sealed class NoOpPushSender : IPushSender
{
    public Task SendAsync(Guid userId, string title, string body, CancellationToken ct) => Task.CompletedTask;
}
