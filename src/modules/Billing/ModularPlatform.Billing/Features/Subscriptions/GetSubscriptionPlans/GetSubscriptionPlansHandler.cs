using Microsoft.Extensions.Options;
using ModularPlatform.Billing.Security;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Subscriptions.GetSubscriptionPlans;

/// <summary>
/// Read slice over the CONFIG plan catalogue (<c>Billing:Subscriptions:Plans</c>). Stripe price ids stay
/// server-side; clients see the stable plan key and what the plan grants.
/// </summary>
internal sealed class GetSubscriptionPlansHandler(IOptions<SubscriptionOptions> options)
    : IQueryHandler<GetSubscriptionPlansQuery, IReadOnlyList<SubscriptionPlanResponse>>
{
    public Task<IReadOnlyList<SubscriptionPlanResponse>> Handle(GetSubscriptionPlansQuery query, CancellationToken ct)
    {
        IReadOnlyList<SubscriptionPlanResponse> plans = options.Value.Plans
            .Select(p => new SubscriptionPlanResponse(p.PlanKey, p.CreditsPerPeriod, p.BucketExpiryDays))
            .ToList();
        return Task.FromResult(plans);
    }
}
