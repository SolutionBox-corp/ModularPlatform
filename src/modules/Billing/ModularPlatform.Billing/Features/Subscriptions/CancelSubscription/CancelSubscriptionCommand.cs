using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.CancelSubscription;

public sealed record CancelSubscriptionCommand(Guid UserId) : ICommand<CancelSubscriptionResponse>;

public sealed record CancelSubscriptionResponse(Guid SubscriptionId, string Status, bool CancelAtPeriodEnd);
