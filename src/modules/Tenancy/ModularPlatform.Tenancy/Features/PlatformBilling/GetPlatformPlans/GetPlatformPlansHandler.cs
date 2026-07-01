using Microsoft.Extensions.Configuration;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.PlatformBilling.GetPlatformPlans;

/// <summary>
/// Read slice over the platform-plane checkout catalogue. Prices stay server-authoritative in
/// Platform:Payments:Plans; the client only chooses a stable plan key.
/// </summary>
internal sealed class GetPlatformPlansHandler(IConfiguration configuration)
    : IQueryHandler<GetPlatformPlansQuery, IReadOnlyList<PlatformPlanResponse>>
{
    public Task<IReadOnlyList<PlatformPlanResponse>> Handle(GetPlatformPlansQuery query, CancellationToken ct)
    {
        IReadOnlyList<PlatformPlanResponse> plans = configuration
            .GetSection("Platform:Payments:Plans")
            .GetChildren()
            .Select(section =>
            {
                var planKey = section.Key.Trim();
                var amount = section.GetValue<long>("AmountMinorUnits");
                var currency = (section["Currency"] ?? "EUR").Trim().ToUpperInvariant();
                var description = section["Description"]?.Trim();
                return new { planKey, amount, currency, description };
            })
            .Where(p => p.planKey.Length > 0 && p.amount > 0)
            .OrderBy(p => p.amount)
            .ThenBy(p => p.planKey)
            .Select(p => new PlatformPlanResponse(
                p.planKey,
                p.amount,
                p.currency,
                string.IsNullOrWhiteSpace(p.description) ? p.planKey : p.description))
            .ToList();

        return Task.FromResult(plans);
    }
}
