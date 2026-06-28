namespace ModularPlatform.Notifications.Features.Notifications.SendNotification;

public sealed record SendNotificationRequest(
    Guid UserId,
    string TemplateKey,
    string[] Channels,
    Dictionary<string, string> Data,
    string? IdempotencyKey = null);
