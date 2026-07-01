using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Payments;

namespace ModularPlatform.Tenancy.Features.Admin.GetPlatformBillingStatus;

/// <summary>
/// Reads the current tenant's entitlement view (live from <c>tenant_entitlements</c> via
/// <see cref="IEntitlementResolver"/>) and projects it as the platform-plane billing status. The tier is the
/// platform-plane "plan"; when the tenant has no explicit tier yet it is reported as <c>"free"</c>.
/// </summary>
internal sealed class GetPlatformBillingStatusHandler(
    ITenantContext tenant,
    IEntitlementResolver entitlements,
    IPaymentGatewayResolver gatewayResolver,
    IEnumerable<IPaymentConfigStore> configStores)
    : IQueryHandler<GetPlatformBillingStatusQuery, PlatformBillingStatusView>
{
    private const string DefaultPlan = "free";

    public async Task<PlatformBillingStatusView> Handle(GetPlatformBillingStatusQuery query, CancellationToken ct)
    {
        var tenantId = tenant.TenantId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        var view = await entitlements.GetForTenantAsync(tenantId, ct);

        var modules = view.Modules
            .Select(m => new PlatformBillingModuleView(m.Key, m.Enabled, m.Tier, m.Limits))
            .ToList();

        var (provider, checkoutReady, actionRequired) = await GetCheckoutStatusAsync(tenantId, ct);

        return new PlatformBillingStatusView(
            tenantId,
            view.Tier ?? DefaultPlan,
            modules,
            provider,
            checkoutReady,
            actionRequired);
    }

    private async Task<(string? Provider, bool CheckoutReady, string? ActionRequired)> GetCheckoutStatusAsync(
        Guid tenantId,
        CancellationToken ct)
    {
        var store = configStores.FirstOrDefault(s => s.Plane == PaymentPlane.Platform);
        var config = store is null ? null : await store.GetAsync(tenantId, PaymentPlane.Platform, ct);
        var provider = config?.Provider.ToString().ToLowerInvariant();

        try
        {
            var gateway = await gatewayResolver.ResolveAsync(tenantId, PaymentPlane.Platform, ct);
            var credentialsValid = await gateway.ValidateCredentialsAsync(ct);
            return credentialsValid
                ? (provider, true, null)
                : (provider, false, "payment.gateway_unreachable");
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            return (provider, false, ex.ErrorCode);
        }
        catch
        {
            return (provider, false, "payment.gateway_unreachable");
        }
    }
}
