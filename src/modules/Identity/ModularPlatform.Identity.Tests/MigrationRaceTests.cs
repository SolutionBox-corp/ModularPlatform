using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Persistence.Rls;
using Npgsql;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// PL-10 — two migration runners can target the same fresh database at the same time. This mirrors two
/// MigrationService/Api startup processes racing during deploy; one runner should do the DDL and the other
/// should observe the completed history without throwing.
/// </summary>
[Collection("Integration")]
public sealed class MigrationRaceTests(PlatformApiFactory fixture)
{
    private const string ModuleName = "Identity";
    private const string HistoryTable = "__ef_migrations_identity";

    [Fact]
    public async Task Parallel_identity_migrations_on_one_fresh_database_are_idempotent()
    {
        var databaseName = $"mp_migration_race_{Guid.CreateVersion7():N}";
        var adminConnectionString = await CreateDatabaseAsync(databaseName);

        try
        {
            var first = PlatformMigrator.MigrateAsync<IdentityDbContext>(
                fixture.Services, adminConnectionString, ModuleName, CancellationToken.None);
            var second = PlatformMigrator.MigrateAsync<IdentityDbContext>(
                fixture.Services, adminConnectionString, ModuleName, CancellationToken.None);

            await Task.WhenAll(first, second);

            var expectedMigrations = await CountKnownMigrationsAsync(adminConnectionString);
            var appliedMigrations = await CountAppliedMigrationsAsync(adminConnectionString);

            appliedMigrations.ShouldBe(expectedMigrations);
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    private async Task<string> CreateDatabaseAsync(string databaseName)
    {
        await using var conn = new NpgsqlConnection(MaintenanceConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""CREATE DATABASE "{databaseName}";""";
        await cmd.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;
    }

    private async Task DropDatabaseAsync(string databaseName)
    {
        await using var conn = new NpgsqlConnection(MaintenanceConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""DROP DATABASE IF EXISTS "{databaseName}" WITH (FORCE);""";
        await cmd.ExecuteNonQueryAsync();
    }

    private string MaintenanceConnectionString() =>
        new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = "postgres",
        }.ConnectionString;

    private static Task<int> CountKnownMigrationsAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable(HistoryTable))
            .Options;

        using var db = new IdentityDbContext(options, new SystemTenantContext());
        return Task.FromResult(db.Database.GetMigrations().Count());
    }

    private static async Task<int> CountAppliedMigrationsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""SELECT count(*)::int FROM "{HistoryTable}";""";
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
}
