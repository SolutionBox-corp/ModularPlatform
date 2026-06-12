namespace ModularPlatform.Abstractions;

/// <summary>
/// The subdomain labels that are NOT tenant workspaces — the platform/control plane. The tenant-resolution
/// middleware passes these through (no tenant lookup) and tenant provisioning refuses them. Kept in ONE place so the
/// two checks can never DRIFT (a label added to the middleware but not the validator could be provisioned yet never
/// routed, and vice-versa).
/// </summary>
public static class ReservedSubdomains
{
    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "admin", "www", "api" };
}
