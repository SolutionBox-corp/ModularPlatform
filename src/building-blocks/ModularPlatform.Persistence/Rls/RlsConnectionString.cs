using Npgsql;

namespace ModularPlatform.Persistence.Rls;

/// <summary>
/// Derives the runtime (RLS-subject) connection string from the admin connection by swapping in the
/// least-privilege role's credentials. Host/port/database stay identical — only the login changes, so the
/// runtime connections hit the same database but as a role that does NOT bypass row-level security.
/// </summary>
public static class RlsConnectionString
{
    /// <summary>
    /// Returns the admin connection unchanged when RLS is disabled; otherwise a copy authenticating as
    /// <see cref="RlsOptions.RuntimeRole"/>.
    /// </summary>
    public static string ForRuntime(string adminConnectionString, RlsOptions options)
    {
        if (!options.Enabled)
        {
            return adminConnectionString;
        }

        return new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = options.RuntimeRole,
            Password = options.RuntimePassword,
        }.ConnectionString;
    }
}
