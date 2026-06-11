using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.GetPlatformBillingStatus;

/// <summary>
/// Reads the current tenant's entitlement view (live from <c>tenant_entitlements</c> via
/// <see cref="IEntitlementResolver"/>) and projects it as the platform-plane billing status. The tier is the
/// platform-plane "plan"; when the tenant has no explicit tier yet it is reported as <c>"free"</c>.
/// </summary>
internal sealed class GetPlatformBillingStatusHandler(ITenantContext tenant, IEntitlementResolver resolver)
    : IQueryHandler<GetPlatformBillingStatusQuery, PlatformBillingStatusView>
{
    private const string DefaultPlan = "free";

    public async Task<PlatformBillingStatusView> Handle(GetPlatformBillingStatusQuery query, CancellationToken ct)
    {
        var tenantId = tenant.TenantId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        var view = await resolver.GetForTenantAsync(tenantId, ct);

        var modules = view.Modules
            .Select(m => new PlatformBillingModuleView(m.Key, m.Enabled, m.Tier))
            .ToList();

        return new PlatformBillingStatusView(tenantId, view.Tier ?? DefaultPlan, modules);
    }
}
