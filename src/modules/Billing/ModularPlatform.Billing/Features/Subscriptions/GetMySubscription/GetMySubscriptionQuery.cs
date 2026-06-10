using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.GetMySubscription;

public sealed record GetMySubscriptionQuery(Guid UserId) : IQuery<SubscriptionResponse>;

public sealed record SubscriptionResponse(
    Guid Id,
    string PlanKey,
    string Status,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);
