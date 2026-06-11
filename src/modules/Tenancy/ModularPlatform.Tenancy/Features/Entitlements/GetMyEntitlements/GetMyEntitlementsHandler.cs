using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Entitlements.GetMyEntitlements;

internal sealed class GetMyEntitlementsHandler(ITenantContext tenant, IEntitlementResolver resolver)
    : IQueryHandler<GetMyEntitlementsQuery, TenantEntitlementsView>
{
    public async Task<TenantEntitlementsView> Handle(GetMyEntitlementsQuery query, CancellationToken ct)
    {
        var tenantId = tenant.TenantId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");
        return await resolver.GetForTenantAsync(tenantId, ct);
    }
}
