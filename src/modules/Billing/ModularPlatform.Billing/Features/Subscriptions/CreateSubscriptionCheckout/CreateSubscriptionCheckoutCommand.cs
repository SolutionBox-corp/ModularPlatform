using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.CreateSubscriptionCheckout;

public sealed record CreateSubscriptionCheckoutCommand(Guid UserId, string PlanKey)
    : ICommand<CreateSubscriptionCheckoutResponse>;

public sealed record CreateSubscriptionCheckoutResponse(string CheckoutSessionId, string CheckoutUrl);

public sealed record CreateSubscriptionCheckoutRequest(string PlanKey);
