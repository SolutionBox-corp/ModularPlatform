using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Gdpr.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Gdpr.Persistence;

/// <summary>
/// Gdpr module's DbContext. Entity configs are discovered from this assembly; xmin concurrency,
/// tenant filter and the per-module audit table are applied by the base.
/// </summary>
internal sealed class GdprDbContext(DbContextOptions<GdprDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "gdpr";

    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<SubjectKey> SubjectKeys => Set<SubjectKey>();
}
