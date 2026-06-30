using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.GetTenant;

/// <summary>
/// Platform-admin (CROSS-TENANT) read: one tenant's registry row PLUS its live per-module entitlements, so the
/// admin UI can render the entitlement editor against the PERSISTED state (not blind). Gated by permission.
/// </summary>
public sealed record GetTenantQuery(Guid TenantId) : IQuery<TenantDetail>;

public sealed record TenantDetail(
    Guid TenantId,
    string Subdomain,
    string Name,
    string Status,
    string RegistrationMode,
    string Placement,
    DateTimeOffset CreatedAt,
    IReadOnlyList<TenantModuleView> Modules);

/// <summary>One module's persisted entitlement for the tenant (enabled = currently granted).</summary>
public sealed record TenantModuleView(string Key, bool Enabled, string? Tier);
