using System.Collections.Concurrent;
using ModularPlatform.Cqrs;
using Stripe;

namespace ModularPlatform.Billing.Stripe;

/// <summary>
/// In-memory Stripe stand-in, registered ONLY under <c>Billing:Stripe:UseFakeGateway=true</c> (the integration
/// test harness). Tests seed events/subscriptions through the public-ish surface below and the FULL worker path
/// (ledger top-up, ProcessedAt, saga transitions) becomes assertable without the network — closing the ST-1/ST-2
/// seam gap. Thread-safe: the suite hits one shared host concurrently.
/// </summary>
internal sealed class FakeStripeGateway : IStripeGateway
{
    private readonly ConcurrentDictionary<string, Event> _events = new();
    private readonly ConcurrentDictionary<string, StripeSubscriptionState> _subscriptions = new();
    private readonly ConcurrentDictionary<string, PromotionCodeState> _promotionCodes = new();
    private readonly ConcurrentQueue<CheckoutSessionSpec> _createdSessions = new();
    private int _sessionCounter;

    public void SeedEvent(Event stripeEvent) => _events[stripeEvent.Id] = stripeEvent;

    public void SeedSubscription(StripeSubscriptionState state) => _subscriptions[state.SubscriptionId] = state;

    public void SeedPromotionCode(PromotionCodeState state) => _promotionCodes[state.Code] = state;

    public IReadOnlyCollection<CheckoutSessionSpec> CreatedSessions => [.. _createdSessions];

    public Task<Event> GetEventAsync(string eventId, CancellationToken ct) =>
        _events.TryGetValue(eventId, out var stripeEvent)
            ? Task.FromResult(stripeEvent)
            : throw new StripeException($"No such event: {eventId}");

    public Task<CheckoutSessionRef> CreateCheckoutSessionAsync(CheckoutSessionSpec spec, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spec.PriceId))
        {
            throw new BusinessRuleException(
                "billing.package.price_not_configured",
                "The package has no Stripe price configured.");
        }

        _createdSessions.Enqueue(spec);
        var id = $"cs_test_{Interlocked.Increment(ref _sessionCounter)}_{Guid.CreateVersion7():N}";
        return Task.FromResult(new CheckoutSessionRef(id, $"https://checkout.stripe.test/{id}"));
    }

    public Task<StripeSubscriptionState?> GetSubscriptionAsync(string subscriptionId, CancellationToken ct) =>
        Task.FromResult(_subscriptions.TryGetValue(subscriptionId, out var state) ? state : null);

    public Task CancelSubscriptionAsync(string subscriptionId, bool atPeriodEnd, CancellationToken ct)
    {
        _subscriptions.AddOrUpdate(
            subscriptionId,
            _ => throw new StripeException($"No such subscription: {subscriptionId}"),
            (_, existing) => atPeriodEnd
                ? existing with { CancelAtPeriodEnd = true }
                : existing with { Status = "canceled" });
        return Task.CompletedTask;
    }

    public Task<PromotionCodeState?> FindActivePromotionCodeAsync(string code, CancellationToken ct) =>
        Task.FromResult(_promotionCodes.TryGetValue(code, out var state) ? state : null);
}
