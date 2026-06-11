using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Tenancy.Persistence;

namespace ModularPlatform.Tenancy.Services;

/// <summary>
/// <see cref="IEntitlementResolver"/> — the per-request "does this tenant have module X?" answer behind the
/// <c>ModuleEntitlementGuard</c> and the FE nav. Reads live from <c>tenant_entitlements</c> (NEVER a JWT claim — a
/// toggle by the platform-admin must take effect on the next request, not after a re-login). Honors the validity window.
/// </summary>
internal sealed class EntitlementResolver(IReadDbContextFactory<TenancyDbContext> readFactory, IClock clock)
    : IEntitlementResolver
{
    public async Task<bool> IsModuleEnabledAsync(Guid tenantId, string moduleKey, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        await using var db = readFactory.Create();
        return await db.TenantEntitlements.AnyAsync(
            e => e.TenantId == tenantId
                 && e.ModuleKey == moduleKey
                 && e.Enabled
                 && (e.ValidFrom == null || e.ValidFrom <= now)
                 && (e.ValidTo == null || e.ValidTo >= now),
            ct);
    }

    public async Task<TenantEntitlementsView> GetForTenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        await using var db = readFactory.Create();
        var rows = await db.TenantEntitlements
            .Where(e => e.TenantId == tenantId)
            .Select(e => new { e.ModuleKey, e.Enabled, e.Tier, e.ValidFrom, e.ValidTo })
            .ToListAsync(ct);

        var modules = rows
            .Select(r => new ModuleEntitlementView(
                r.ModuleKey,
                r.Enabled && (r.ValidFrom is null || r.ValidFrom <= now) && (r.ValidTo is null || r.ValidTo >= now),
                r.Tier))
            .ToList();

        // The tenant-level plan tier (platform-plane subscription) is layered on later; per-module tier lives above.
        return new TenantEntitlementsView(tenantId, Tier: null, Modules: modules);
    }
}
