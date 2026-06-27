using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Persistence;

/// <summary>
/// CRM module's DbContext. Entity configs are discovered from this assembly; xmin concurrency, the tenant
/// filter, the soft-delete filter and the per-module audit table are applied by <see cref="PlatformDbContext"/>.
/// No DbSets yet — the first one (Contacts) arrives with the Contacts feature (Phase 1). This scaffolding exists
/// so the module is wired into all four hosts and the migration/RLS pipeline before any domain logic lands.
/// </summary>
internal sealed class CrmDbContext(DbContextOptions<CrmDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "crm";
}
