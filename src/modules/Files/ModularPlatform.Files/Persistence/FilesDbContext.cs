using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Files.Entities;
using ModularPlatform.Persistence;

namespace ModularPlatform.Files.Persistence;

/// <summary>
/// Files module DbContext. xmin concurrency, the per-module audit table and the IUserOwned RLS policy on
/// <c>file_objects</c> are applied by the base / bootstrapper.
/// </summary>
internal sealed class FilesDbContext(DbContextOptions<FilesDbContext> options, ITenantContext tenant)
    : PlatformDbContext(options, tenant)
{
    public override string ModuleName => "files";

    public DbSet<FileObject> Files => Set<FileObject>();
    public DbSet<FileLink> FileLinks => Set<FileLink>();
}
