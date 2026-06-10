using Wolverine;

namespace ModularPlatform.Billing.Sagas;

// Saga messages are PUBLIC (Wolverine codegen + durable serialization) but module-internal in meaning —
// they never leave Billing. `Id` is the purchase id and the saga identity (Wolverine convention).

/// <summary>Starts the purchase saga right after the Stripe Checkout session is created (outboxed by the accept handler).</summary>
public sealed record CreditPurchaseStarted(
    Guid Id,
    Guid UserId,
    Guid PackageId,
    string CheckoutSessionId,
    long CreditAmount,
    int? BucketExpiryDays,
    int TimeoutMinutes);

/// <summary>
/// The Stripe `checkout.session.completed` fact, reduced to what the grant needs. Carries the full grant
/// payload so a LATE payment (saga already timed out and deleted) is still honored via the static
/// <c>NotFound</c> path — a paid customer NEVER loses credits.
/// </summary>
public sealed record CreditPurchaseConfirmed(
    Guid Id,
    Guid UserId,
    long CreditAmount,
    int? BucketExpiryDays,
    string StripeEventId);

/// <summary>Scheduled abandon timeout — delivered after the configured checkout window elapses.</summary>
public sealed record CreditPurchaseTimeout(Guid Id, int Minutes)
    : TimeoutMessage(TimeSpan.FromMinutes(Minutes));
