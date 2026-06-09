using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModularPlatform.Abstractions;

namespace ModularPlatform.Persistence.Rls;

/// <summary>
/// Stamps the current request principal onto every runtime connection as Postgres session GUCs, which the
/// RLS policies read: <c>app.is_system</c> (system principals — worker/jobs — bypass the policy),
/// <c>app.principal_id</c> (the owning user) and <c>app.tenant_id</c>. Set on EVERY connection open (never
/// left stale) so a pooled connection reused across principals cannot leak — and Npgsql also resets session
/// state on pool return. Runs as the session role, before any query on that connection.
/// </summary>
public sealed class PrincipalSessionConnectionInterceptor(ITenantContext tenant) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyAsync(connection, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        // is_local = false (session scope): set on a fresh connection before any transaction, so it survives
        // across the EF operations on this connection and is cleared on pool return.
        command.CommandText =
            "SELECT set_config('app.is_system', @is_system, false), " +
            "set_config('app.principal_id', @principal, false), " +
            "set_config('app.tenant_id', @tenant, false)";
        AddParam(command, "is_system", tenant.IsSystem ? "on" : "off");
        AddParam(command, "principal", tenant.UserId?.ToString() ?? string.Empty);
        AddParam(command, "tenant", tenant.TenantId?.ToString() ?? string.Empty);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void AddParam(DbCommand command, string name, string value)
    {
        var p = command.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        command.Parameters.Add(p);
    }
}
