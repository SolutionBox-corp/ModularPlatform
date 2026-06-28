using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Contracts;

/// <summary>
/// Outbound notification request. Persists an in-app feed row and hands off durable per-channel delivery work to the
/// Worker via the outbox. Channels are "email" | "push" | "inapp"; data fills the template {placeholders}.
/// </summary>
public sealed record SendNotificationCommand(
    Guid UserId,
    string TemplateKey,
    string[] Channels,
    Dictionary<string, string> Data,
    string? IdempotencyKey = null) : ICommand<Unit>;
