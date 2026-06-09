namespace ModularPlatform.Persistence.Rls;

/// <summary>
/// Postgres Row-Level-Security defence-in-depth. PostgreSQL never applies RLS to a superuser or a table
/// owner, so the application's DATA connections must use a dedicated least-privilege role (<see cref="RuntimeRole"/>)
/// distinct from the admin role that runs migrations + owns the schema. The configured
/// <c>ConnectionStrings:Write/Read</c> stay the ADMIN role (migrations, Wolverine, bootstrap); the module
/// DbContexts derive a runtime connection that swaps in this role. The bootstrapper provisions the role and
/// its grants idempotently at startup, so dev/test need no manual DBA step.
/// </summary>
public sealed class RlsOptions
{
    public const string SectionName = "Persistence:Rls";

    /// <summary>When false, RLS is off: module contexts use the admin connection and no policies are created.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The least-privilege login role the runtime data connections use (subject to RLS).</summary>
    public string RuntimeRole { get; set; } = "app_rls";

    /// <summary>The dev/test placeholder password — the host refuses to start with this outside Development.</summary>
    public const string DevPasswordPlaceholder = "dev_app_rls_password_change_me";

    /// <summary>
    /// Password the bootstrapper sets on <see cref="RuntimeRole"/> and the runtime connection authenticates with.
    /// Override from a secret in production; the default is a dev/test placeholder only.
    /// </summary>
    public string RuntimePassword { get; set; } = DevPasswordPlaceholder;
}
