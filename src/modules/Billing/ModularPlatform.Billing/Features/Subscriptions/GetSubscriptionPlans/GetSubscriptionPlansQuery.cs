using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.GetSubscriptionPlans;

public sealed record GetSubscriptionPlansQuery : IQuery<IReadOnlyList<SubscriptionPlanResponse>>;

public sealed record SubscriptionPlanResponse(string PlanKey, long CreditsPerPeriod, int? BucketExpiryDays);
