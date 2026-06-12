using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Features.Notifications.SendNotification;

/// <summary>
/// Outbound notification request. Persists an in-app feed row and hands off durable per-channel delivery
/// work to the Worker via the outbox — never sends inline in the HTTP request. Channels are
/// "email" | "push" | "inapp"; data fills the template {placeholders}.
/// </summary>
public sealed record SendNotificationCommand(
    Guid UserId,
    string TemplateKey,
    string[] Channels,
    Dictionary<string, string> Data,
    // Optional dedup key. When set, a UNIQUE index makes the send exactly-once — a combined-envelope retry (the
    // UserRegistered fan-out runs Billing + this handler in one envelope; a Billing-side throw re-runs both) cannot
    // create a duplicate feed row / duplicate email. Null = no dedup (a notification that may legitimately repeat).
    string? IdempotencyKey = null) : ICommand<Unit>;

public sealed record SendNotificationRequest(
    Guid UserId,
    string TemplateKey,
    string[] Channels,
    Dictionary<string, string> Data);
