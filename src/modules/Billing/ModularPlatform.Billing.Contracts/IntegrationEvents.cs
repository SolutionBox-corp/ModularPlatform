using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Contracts;

/// <summary>
/// Published (via the outbox) after a credit top-up is durably applied to the ledger. Other modules
/// (e.g. Notifications) subscribe to confirm the purchase. Amounts are the smallest credit unit (long).
/// </summary>
public sealed record CreditsToppedUpIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid AccountId,
    long Amount,
    long NewPosted,
    string IdempotencyKey) : IIntegrationEvent;

/// <summary>
/// Published (via the outbox) when reserved credits are confirmed as spent (posted debit). Consumers
/// can react to actual consumption — metering, receipts, downstream provisioning.
/// </summary>
public sealed record CreditsSpentIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid AccountId,
    Guid ReservationId,
    long Amount,
    long NewPosted) : IIntegrationEvent;

/// <summary>
/// Published after a Stripe payment is ingested and the corresponding top-up is durably applied to the
/// credit ledger. Consumers (e.g. Notifications) can send a purchase-confirmation message. The
/// <see cref="CreditAmount"/> is the number of credits topped up (smallest unit, long).
/// </summary>
public sealed record CreditPurchaseCompletedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    long CreditAmount,
    string IdempotencyKey) : IIntegrationEvent;
