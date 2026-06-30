using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using Npgsql;

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
        var advisoryLockKey = AdvisoryLockKey(adminConnectionString, moduleName);

        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(adminConnectionString, npg => npg.MigrationsHistoryTable(historyTable))
            .Options;

        await using var migrationLock = new NpgsqlConnection(adminConnectionString);
        await migrationLock.OpenAsync(ct);
        await ExecuteAdvisoryLockAsync(migrationLock, advisoryLockKey, ct);
        await using var ctx = (TContext)Activator.CreateInstance(typeof(TContext), options, tenant)!;
        try
        {
            await ctx.Database.MigrateAsync(ct);
        }
        finally
        {
            await ExecuteAdvisoryUnlockAsync(migrationLock, advisoryLockKey);
        }
    }

    private static long AdvisoryLockKey(string connectionString, string moduleName)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? string.Empty;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"modularplatform:migration:{database}:{moduleName}"));
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    private static async Task ExecuteAdvisoryLockAsync(NpgsqlConnection connection, long key, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_lock(@key);";
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ExecuteAdvisoryUnlockAsync(NpgsqlConnection connection, long key)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT pg_advisory_unlock(@key);";
        cmd.Parameters.AddWithValue("key", key);
        await cmd.ExecuteNonQueryAsync();
    }
}
