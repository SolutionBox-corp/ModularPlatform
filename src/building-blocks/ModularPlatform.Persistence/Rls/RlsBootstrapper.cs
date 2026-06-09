using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModularPlatform.Persistence.Entities;
using Npgsql;

namespace ModularPlatform.Persistence.Rls;

/// <summary>
/// Idempotently provisions Postgres Row-Level Security as defence-in-depth, run once at startup by the host
/// AFTER migrations (the tables must exist). Steps, all re-runnable: (1) ensure the least-privilege runtime
/// role + its grants, (2) set default privileges so tables created later (e.g. Wolverine's outbox) auto-grant
/// to the role, (3) ENABLE + FORCE RLS and (re)create a principal policy on every <see cref="IUserOwned"/>
/// table discovered from the module models. A forgotten <c>WHERE UserId == …</c> then still cannot leak rows.
/// </summary>
public static class RlsBootstrapper
{
    private const string PolicyName = "rls_principal";
    private const string OwnerColumn = "UserId";
    private static readonly Regex SafeIdentifier = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static async Task ApplyAsync(IServiceProvider services, string adminConnectionString, CancellationToken ct)
    {
        var options = services.GetRequiredService<IOptions<RlsOptions>>().Value;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ModularPlatform.Rls");
        if (!options.Enabled)
        {
            logger.LogInformation("RLS disabled (Persistence:Rls:Enabled=false) — runtime uses the admin connection, no policies applied.");
            return;
        }

        if (!SafeIdentifier.IsMatch(options.RuntimeRole))
        {
            throw new InvalidOperationException($"Invalid RLS runtime role name '{options.RuntimeRole}'.");
        }

        // Secrets fail-fast: refuse to start with the dev placeholder password outside Development — it would
        // make the least-privilege role's credentials public knowledge. Provide a real secret (env/KeyVault).
        var environment = services.GetService<IHostEnvironment>();
        if (environment is not null && !environment.IsDevelopment()
            && options.RuntimePassword == RlsOptions.DevPasswordPlaceholder)
        {
            throw new InvalidOperationException(
                "Persistence:Rls:RuntimePassword is the dev placeholder — set a real secret outside Development "
                + "(or set Persistence:Rls:Enabled=false on a DB where you cannot provision roles).");
        }

        var tables = CollectUserOwnedTables(services);

        await using var conn = new NpgsqlConnection(adminConnectionString);
        await conn.OpenAsync(ct);

        await ExecuteAsync(conn, BuildRoleAndGrantsSql(options), ct);
        foreach (var table in tables)
        {
            await ExecuteAsync(conn, BuildPolicySql(table), ct);
        }

        logger.LogInformation(
            "RLS applied: role {Role} provisioned; {Count} user-owned table(s) protected ({Tables}).",
            options.RuntimeRole, tables.Count, string.Join(", ", tables));
    }

    /// <summary>Distinct quoted "schema"."table" identifiers for every IUserOwned entity across module models.</summary>
    private static List<string> CollectUserOwnedTables(IServiceProvider services)
    {
        var tables = new SortedSet<string>(StringComparer.Ordinal);
        using var scope = services.CreateScope();

        foreach (var managed in services.GetServices<RlsManagedContext>())
        {
            var ctx = (DbContext)scope.ServiceProvider.GetRequiredService(managed.ContextType);
            foreach (var entity in ctx.Model.GetEntityTypes())
            {
                if (!typeof(IUserOwned).IsAssignableFrom(entity.ClrType))
                {
                    continue;
                }

                var table = entity.GetTableName();
                if (table is null)
                {
                    continue;
                }

                var schema = entity.GetSchema() ?? "public";
                tables.Add($"\"{schema}\".\"{table}\"");
            }
        }

        return tables.ToList();
    }

    private static string BuildRoleAndGrantsSql(RlsOptions options)
    {
        var role = options.RuntimeRole; // validated identifier
        var password = options.RuntimePassword.Replace("'", "''");

        return $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{role}') THEN
                    CREATE ROLE "{role}" LOGIN NOSUPERUSER NOBYPASSRLS;
                END IF;
            END $$;
            ALTER ROLE "{role}" WITH LOGIN NOSUPERUSER NOBYPASSRLS PASSWORD '{password}';
            -- Pre-create Wolverine's message-store schema so its tables (created later at host start, as the admin
            -- role) inherit the default privileges below. Without this the runtime role can't write the outbox.
            CREATE SCHEMA IF NOT EXISTS wolverine;
            {BuildSchemaGrantsSql("public", role)}
            {BuildSchemaGrantsSql("wolverine", role)}
            """;
    }

    private static string BuildSchemaGrantsSql(string schema, string role) =>
        $"""
        GRANT USAGE ON SCHEMA {schema} TO "{role}";
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO "{role}";
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA {schema} TO "{role}";
        ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO "{role}";
        ALTER DEFAULT PRIVILEGES IN SCHEMA {schema} GRANT USAGE, SELECT ON SEQUENCES TO "{role}";
        """;

    private static string BuildPolicySql(string quotedTable)
    {
        // System principals (worker/jobs/migration) bypass via app.is_system; users see only rows they own.
        // NULLIF + missing_ok current_setting => an unset/empty principal matches nothing (anonymous sees zero rows).
        const string predicate =
            "current_setting('app.is_system', true) = 'on' " +
            $"OR \"{OwnerColumn}\" = NULLIF(current_setting('app.principal_id', true), '')::uuid";

        return $"""
            ALTER TABLE {quotedTable} ENABLE ROW LEVEL SECURITY;
            ALTER TABLE {quotedTable} FORCE ROW LEVEL SECURITY;
            DROP POLICY IF EXISTS {PolicyName} ON {quotedTable};
            CREATE POLICY {PolicyName} ON {quotedTable}
                USING ({predicate})
                WITH CHECK ({predicate});
            """;
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
