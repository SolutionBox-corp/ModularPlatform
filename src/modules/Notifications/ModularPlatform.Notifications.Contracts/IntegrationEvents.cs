using ModularPlatform.Cqrs;

namespace ModularPlatform.Notifications.Contracts;

/// <summary>
/// Durable channel-delivery work, published to the outbox by the SendNotification handler and consumed
/// by the Worker — one message per requested channel ("email"/"push"). In-app delivery is handled inline
/// (the row is persisted + a realtime push fired), so it does NOT get a delivery message here.
/// Never delivered inline in the HTTP request: the handler persists the in-app row and hands off these
/// messages, the Worker performs the actual SMTP / push send.
/// </summary>
public sealed record EmailDeliveryRequested(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid NotificationId,
    Guid UserId,
    string ToAddress,
    string Subject,
    string Body) : IIntegrationEvent;

/// <summary>Durable push-channel delivery work, consumed by the Worker (stub sender for now).</summary>
public sealed record PushDeliveryRequested(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid NotificationId,
    Guid UserId,
    string Title,
    string Body) : IIntegrationEvent;
