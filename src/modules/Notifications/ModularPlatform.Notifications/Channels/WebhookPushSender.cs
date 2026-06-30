using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace ModularPlatform.Notifications.Channels;

internal sealed class PushOptions
{
    public const string SectionName = "Notifications:Push";

    public string? WebhookUrl { get; init; }

    public int TimeoutSeconds { get; init; } = 5;
}

/// <summary>
/// Provider-agnostic push delivery. The platform sends a stable payload to an infrastructure-owned webhook;
/// that webhook can bridge to FCM, APNS, Expo, OneSignal, or a tenant-specific delivery service.
/// </summary>
internal sealed class WebhookPushSender(
    IHttpClientFactory httpClientFactory,
    IOptions<PushOptions> options) : IPushSender
{
    public async Task SendAsync(Guid userId, string title, string body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.WebhookUrl))
        {
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.Value.TimeoutSeconds)));

        var client = httpClientFactory.CreateClient("notifications-push");
        using var response = await client.PostAsJsonAsync(
            options.Value.WebhookUrl,
            new PushWebhookPayload(userId, title, body),
            timeout.Token);
        response.EnsureSuccessStatusCode();
    }
}

internal sealed record PushWebhookPayload(Guid UserId, string Title, string Body);
