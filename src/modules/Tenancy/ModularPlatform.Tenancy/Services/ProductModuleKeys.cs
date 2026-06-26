namespace ModularPlatform.Tenancy.Services;

/// <summary>
/// Tenant entitlement keys the platform knows how to sell/enable. New product modules add their key here, but they do
/// not become enabled for fresh tenants unless they are also in <see cref="DefaultEntitled"/>.
/// </summary>
internal static class ProductModuleKeys
{
    public static readonly string[] DefaultEntitled =
        ["billing", "notifications", "files", "operations", "gdpr", "marketing"];

    private static readonly HashSet<string> Known = new(DefaultEntitled, StringComparer.Ordinal)
    {
        "crm",
    };

    public static bool IsKnown(string moduleKey) => Known.Contains(moduleKey);
}
