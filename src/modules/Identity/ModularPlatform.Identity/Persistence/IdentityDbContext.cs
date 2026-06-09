using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Identity.Persistence;

/// <summary>
/// Identity module's DbContext. Entity configs are discovered from this assembly; xmin concurrency,
/// tenant filter, soft-delete filter and the per-module audit table are applied by the base.
/// </summary>
internal sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "identity";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
}
