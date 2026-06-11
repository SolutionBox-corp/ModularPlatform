using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Tenancy.Persistence;

namespace ModularPlatform.Tenancy.Services;

/// <summary>
/// <see cref="ITenantDirectory"/> over the registry. Reads through the no-tracking read context. The registry is not
/// tenant-scoped, so subdomain resolution works pre-auth (the host has no tenant yet) and across tenants.
/// </summary>
internal sealed class TenantDirectory(IReadDbContextFactory<TenancyDbContext> readFactory) : ITenantDirectory
{
    public async Task<TenantInfo?> FindBySubdomainAsync(string subdomain, CancellationToken ct = default)
    {
        var key = subdomain.Trim().ToLowerInvariant();
        await using var db = readFactory.Create();
        return await db.Tenants
            .Where(t => t.Subdomain == key)
            .Select(t => new TenantInfo(t.Id, t.Subdomain, t.Name, t.Status.ToString(), t.Placement))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TenantInfo?> GetByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        await using var db = readFactory.Create();
        return await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantInfo(t.Id, t.Subdomain, t.Name, t.Status.ToString(), t.Placement))
            .FirstOrDefaultAsync(ct);
    }
}
