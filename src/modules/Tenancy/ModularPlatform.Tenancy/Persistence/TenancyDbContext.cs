using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;
using ModularPlatform.Tenancy.Entities;

namespace ModularPlatform.Tenancy.Persistence;

/// <summary>
/// Tenancy module's DbContext — owns the platform tenant registry (<c>tenants</c>) and per-tenant module
/// entitlements (<c>tenant_entitlements</c>). Neither entity is tenant-scoped (the registry IS the tenant list),
/// so the base tenant query filter does not apply here; xmin concurrency + the per-module audit table do.
/// </summary>
internal sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "tenancy";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantEntitlement> TenantEntitlements => Set<TenantEntitlement>();
    public DbSet<TenantInvite> TenantInvites => Set<TenantInvite>();
}
