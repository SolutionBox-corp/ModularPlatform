using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Persistence.Rls;

/// <summary>
/// Applies a module's EF Core migrations on the ADMIN connection. The DI-registered module DbContext uses
/// the least-privilege runtime role (subject to RLS), which cannot run DDL or own the schema — so migrations
/// must build their own context on the admin connection instead of resolving the runtime one. The migrations
/// history table name MUST match the one <c>AddModuleDbContext</c> configured for the runtime context.
/// </summary>
public static class PlatformMigrator
{
    public static async Task MigrateAsync<TContext>(
        IServiceProvider services, string adminConnectionString, string moduleName, CancellationToken ct)
        where TContext : PlatformDbContext
    {
        var tenant = services.GetRequiredService<ITenantContext>();
        var historyTable = $"__ef_migrations_{moduleName.ToLowerInvariant()}";

        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(adminConnectionString, npg => npg.MigrationsHistoryTable(historyTable))
            .Options;

        await using var ctx = (TContext)Activator.CreateInstance(typeof(TContext), options, tenant)!;
        await ctx.Database.MigrateAsync(ct);
    }
}
