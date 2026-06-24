using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;
using ModularPlatform.Tenancy.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.GetTenant;

/// <summary>
/// Reads one tenant from the registry + its live entitlements (via <see cref="IEntitlementResolver"/>, the same
/// source the guard/nav use). Cross-tenant: scope is the permission, not the token subject.
/// </summary>
internal sealed class GetTenantHandler(
    IReadDbContextFactory<TenancyDbContext> readFactory,
    IEntitlementResolver resolver)
    : IQueryHandler<GetTenantQuery, TenantDetail>
{
    public async Task<TenantDetail> Handle(GetTenantQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var tenant = await db.Tenants
            .Where(t => t.Id == query.TenantId)
            .Select(t => new
            {
                t.Id,
                t.Subdomain,
                t.Name,
                Status = t.Status.ToString(),
                t.Placement,
                t.CreatedAt,
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("tenant.not_found", "Workspace not found.");

        var entitlements = await resolver.GetForTenantAsync(query.TenantId, ct);
        var modules = entitlements.Modules
            .Select(m => new TenantModuleView(m.Key, m.Enabled, m.Tier))
            .ToList();

        return new TenantDetail(
            tenant.Id, tenant.Subdomain, tenant.Name, tenant.Status, tenant.Placement, tenant.CreatedAt, modules);
    }
}
