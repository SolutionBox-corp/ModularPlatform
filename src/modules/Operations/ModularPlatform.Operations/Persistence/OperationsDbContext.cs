using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Operations.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Operations.Persistence;

/// <summary>
/// Operations module DbContext. xmin concurrency, the per-module audit table and the IUserOwned RLS policy on
/// <c>operations</c> are applied by the base / bootstrapper.
/// </summary>
internal sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "operations";

    public DbSet<Operation> Operations => Set<Operation>();
}
