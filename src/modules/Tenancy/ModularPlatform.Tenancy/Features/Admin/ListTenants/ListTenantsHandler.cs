using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Persistence;
using ModularPlatform.Tenancy.Persistence;

namespace ModularPlatform.Tenancy.Features.Admin.ListTenants;

/// <summary>
/// CROSS-TENANT tenant listing for platform admins. The <c>tenants</c> registry is not tenant-scoped (it IS the
/// tenant list), so there is no per-tenant query filter to bypass. Paged, newest first.
/// </summary>
internal sealed class ListTenantsHandler(IReadDbContextFactory<TenancyDbContext> readFactory)
    : IQueryHandler<ListTenantsQuery, TenantsResponse>
{
    public async Task<TenantsResponse> Handle(ListTenantsQuery query, CancellationToken ct)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var offset = Math.Max(query.Offset, 0);

        await using var db = readFactory.Create();

        var total = await db.Tenants.CountAsync(ct);

        var items = await db.Tenants
            .OrderByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .Select(t => new TenantItem(
                t.Id,
                t.Subdomain,
                t.Name,
                t.Status.ToString(),
                t.Placement,
                t.CreatedAt))
            .ToListAsync(ct);

        return new TenantsResponse(items, total, limit, offset);
    }
}
