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
/// Published when a credit-package purchase completes end-to-end (Stripe Checkout confirmed, credits granted
/// by the purchase saga). Consumers: Notifications (purchase receipt), analytics.
/// </summary>
public sealed record CreditPurchaseCompletedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    Guid PurchaseId,
    Guid PackageId,
    long CreditAmount) : IIntegrationEvent;

/// <summary>Published when a Stripe subscription reaches Active locally (first activation or recovery).</summary>
public sealed record SubscriptionActivatedIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    string PlanKey,
    DateTimeOffset? CurrentPeriodEnd) : IIntegrationEvent;

/// <summary>Published when a Stripe subscription ends (canceled in Stripe, mirrored locally).</summary>
public sealed record SubscriptionCanceledIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    string PlanKey) : IIntegrationEvent;

/// <summary>Published when a Stripe subscription first enters PastDue locally (failed renewal / dunning).</summary>
public sealed record SubscriptionPastDueIntegrationEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid UserId,
    string PlanKey,
    DateTimeOffset? CurrentPeriodEnd) : IIntegrationEvent;
