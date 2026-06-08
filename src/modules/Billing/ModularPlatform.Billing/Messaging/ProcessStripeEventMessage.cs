namespace ModularPlatform.Billing.Messaging;

/// <summary>
/// Internal Wolverine message enqueued by the webhook AFTER the StripeEvent row is committed. The actual
/// idempotent ledger top-up runs in the Worker (never inline in the HTTP request). Carries the Stripe event
/// id so the handler can resolve it and reconcile against object state — events may arrive out of order.
/// </summary>
public sealed record ProcessStripeEventMessage(string StripeEventId, string Type);
