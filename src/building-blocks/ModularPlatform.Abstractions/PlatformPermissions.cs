using System.Reflection;

namespace ModularPlatform.Abstractions;

/// <summary>
/// The platform's permission vocabulary — the single source of truth for fine-grained authorization. Each
/// permission is a stable dotted string used both as a JWT <c>permission</c> claim and as the key an endpoint
/// gates on via <c>.RequirePermission(...)</c>. The Identity seeder upserts <see cref="All"/> into the
/// permissions table on startup and grants every one of them to the system <c>admin</c> role.
///
/// To add a permission: add a <c>const</c> here, gate the endpoint with it, and assign it to a role (the admin
/// role gets it automatically on next boot). Keep names <c>module.action</c> (lowercase, snake within action).
/// </summary>
public static class PlatformPermissions
{
    /// <summary>Assign/revoke roles to users (admin user management).</summary>
    public const string IdentityManageRoles = "identity.manage_roles";

    /// <summary>Send a notification to an arbitrary user (system/admin operation, not self-service).</summary>
    public const string NotificationsSend = "notifications.send";

    /// <summary>Read a user's audit trail with personal-data values decrypted (admin forensics, until erasure).</summary>
    public const string AuditRead = "audit.read";

    /// <summary>Manage the Billing catalogue (credit packages) — admin commerce operations.</summary>
    public const string BillingManage = "billing.manage";

    /// <summary>Platform-admin: provision tenants, toggle their module entitlements, suspend them (cross-tenant control plane).</summary>
    public const string PlatformTenantsManage = "platform.tenants.manage";

    /// <summary>Platform-admin: mint a tenant-scoped MACHINE principal (a non-user service token for edge agents / device gateways).</summary>
    public const string MachineTokensIssue = "platform.machine_tokens";

    /// <summary>Every declared permission, discovered by reflection over the public string consts above.</summary>
    public static IReadOnlyList<string> All { get; } = typeof(PlatformPermissions)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string))
        .Select(f => (string)f.GetValue(null)!)
        .ToArray();
}
