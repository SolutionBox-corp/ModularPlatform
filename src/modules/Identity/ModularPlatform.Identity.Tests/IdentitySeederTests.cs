using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Identity.Authorization;
using ModularPlatform.Identity.Persistence;
using ModularPlatform.IntegrationTesting;
using ModularPlatform.Persistence.Rls;
using Npgsql;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Startup authorization seeding: permissions catalog, system admin role links, and configured-admin assignment.
/// These tests drive the real hosted seeder by starting a derived host over the shared test database.
/// </summary>
[Collection("Integration")]
public sealed class IdentitySeederTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Startup_seeder_does_not_regrant_admin_to_a_soft_deleted_configured_admin()
    {
        var email = $"deleted-admin-{Guid.CreateVersion7():N}@x.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        await fixture.ExecuteSqlAsync(
            $"""UPDATE users SET "DeletedAt" = now() WHERE "Id" = '{userId}'""");

        using var host = fixture.CreateHost(("Identity:Auth:AdminEmails:1", email));
        using var client = host.CreateClient();

        var adminAssignments = await fixture.ScalarAsync<long>(
            $$"""
              SELECT count(*)::bigint
              FROM user_roles ur
              JOIN roles r ON r."Id" = ur."RoleId"
              WHERE ur."UserId" = '{{userId}}' AND r."Name" = 'admin'
              """);
        adminAssignments.ShouldBe(0);
    }

    [Fact]
    public async Task Login_time_admin_bootstrap_does_not_grant_admin_to_a_soft_deleted_configured_admin()
    {
        var email = $"deleted-login-admin-{Guid.CreateVersion7():N}@x.com";
        using var host = fixture.CreateHost(("Identity:Auth:AdminEmails:1", email));
        using var client = host.CreateClient();

        var register = await client.PostAsJsonAsync(
            "/v1/identity/users",
            new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        await fixture.ExecuteSqlAsync(
            $"""UPDATE users SET "DeletedAt" = now() WHERE "Id" = '{userId}'""");

        var login = await client.PostAsJsonAsync(
            "/v1/identity/auth/login",
            new { email, password = Password });

        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var adminAssignments = await fixture.ScalarAsync<long>(
            $$"""
              SELECT count(*)::bigint
              FROM user_roles ur
              JOIN roles r ON r."Id" = ur."RoleId"
              WHERE ur."UserId" = '{{userId}}' AND r."Name" = 'admin'
              """);
        adminAssignments.ShouldBe(0);
    }

    [Fact]
    public async Task Startup_seeder_relinks_missing_platform_permissions_to_the_admin_role_on_reboot()
    {
        var permissionName = PlatformPermissions.NotificationsSend;
        await fixture.ExecuteSqlAsync(
            $$"""
              DELETE FROM role_permissions rp
              USING roles r, permissions p
              WHERE rp."RoleId" = r."Id"
                AND rp."PermissionId" = p."Id"
                AND r."Name" = 'admin'
                AND p."Name" = '{{permissionName}}'
              """);

        var missingBefore = await AdminRolePermissionLinkCountAsync(permissionName);
        missingBefore.ShouldBe(0);

        using var host = fixture.CreateHost();
        using var client = host.CreateClient();

        var restored = await AdminRolePermissionLinkCountAsync(permissionName);
        restored.ShouldBe(1);
    }

    [Fact]
    public async Task Startup_seeder_tolerates_missing_identity_tables_before_migrations_finish()
    {
        var databaseName = $"mp_identity_seeder_missing_tables_{Guid.CreateVersion7():N}";
        var connectionString = await CreateDatabaseAsync(databaseName);

        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITenantContext>(new SystemTenantContext());
            services.AddSingleton<IBlindIndexHasher>(new TestBlindIndexHasher());
            services.AddDbContext<IdentityDbContext>(options => options.UseNpgsql(connectionString));

            await using var provider = services.BuildServiceProvider();
            var seeder = new IdentitySeeder(
                provider,
                Options.Create(new IdentityAuthOptions
                {
                    AdminEmails = [PlatformApiFactory.AdminEmail],
                }),
                NullLogger<IdentitySeeder>.Instance);

            await Should.NotThrowAsync(() => seeder.StartAsync(CancellationToken.None));
        }
        finally
        {
            await DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Concurrent_startup_seeders_leave_one_complete_authorization_model()
    {
        var email = $"concurrent-admin-{Guid.CreateVersion7():N}@x.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var permissionCount = PlatformPermissions.All.Distinct().Count();

        await fixture.ExecuteSqlAsync("DELETE FROM user_roles");
        await fixture.ExecuteSqlAsync("DELETE FROM role_permissions");
        await fixture.ExecuteSqlAsync("DELETE FROM roles");
        await fixture.ExecuteSqlAsync("DELETE FROM permissions");

        var hosts = Enumerable.Range(0, 4)
            .Select(_ => fixture.CreateHost(
                ("RunMigrationsAtStartup", "false"),
                ("Identity:Auth:AdminEmails:1", email)))
            .ToArray();
        try
        {
            var clients = await Task.WhenAll(hosts.Select(host => Task.Run(host.CreateClient)));
            foreach (var client in clients)
            {
                client.Dispose();
            }

            var adminRoleCount = await fixture.ScalarAsync<long>(
                """SELECT count(*)::bigint FROM roles WHERE "Name" = 'admin'""");
            adminRoleCount.ShouldBe(1);

            var duplicatePermissionNames = await fixture.ScalarAsync<long>(
                """
                SELECT count(*)::bigint
                FROM (
                    SELECT "Name"
                    FROM permissions
                    GROUP BY "Name"
                    HAVING count(*) > 1
                ) duplicates
                """);
            duplicatePermissionNames.ShouldBe(0);

            var seededPermissions = await fixture.ScalarAsync<long>(
                "SELECT count(*)::bigint FROM permissions");
            seededPermissions.ShouldBe(permissionCount);

            var adminLinks = await fixture.ScalarAsync<long>(
                """
                SELECT count(*)::bigint
                FROM role_permissions rp
                JOIN roles r ON r."Id" = rp."RoleId"
                WHERE r."Name" = 'admin'
                """);
            adminLinks.ShouldBe(permissionCount);

            var adminAssignments = await fixture.ScalarAsync<long>(
                $$"""
                  SELECT count(*)::bigint
                  FROM user_roles ur
                  JOIN roles r ON r."Id" = ur."RoleId"
                  WHERE ur."UserId" = '{{userId}}' AND r."Name" = 'admin'
                  """);
            adminAssignments.ShouldBe(1);
        }
        finally
        {
            foreach (var host in hosts)
            {
                host.Dispose();
            }
        }
    }

    private async Task<long> AdminRolePermissionLinkCountAsync(string permissionName) =>
        await fixture.ScalarAsync<long>(
            $$"""
              SELECT count(*)::bigint
              FROM role_permissions rp
              JOIN roles r ON r."Id" = rp."RoleId"
              JOIN permissions p ON p."Id" = rp."PermissionId"
              WHERE r."Name" = 'admin'
                AND p."Name" = '{{permissionName}}'
              """);

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

    private sealed class TestBlindIndexHasher : IBlindIndexHasher
    {
        public string Hash(string normalizedValue) => normalizedValue;
    }
}
