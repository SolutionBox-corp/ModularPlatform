using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.PlatformBilling.GetPlatformPlans;

public sealed record GetPlatformPlansQuery : IQuery<IReadOnlyList<PlatformPlanResponse>>;

public sealed record PlatformPlanResponse(
    string PlanKey,
    long AmountMinorUnits,
    string Currency,
    string Description);
