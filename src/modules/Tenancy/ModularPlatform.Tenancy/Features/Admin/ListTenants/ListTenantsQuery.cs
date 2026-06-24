using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.ListTenants;

/// <summary>
/// Platform-admin (CROSS-TENANT) read: list every tenant in the registry, paged. The registry is not
/// tenant-scoped, so no per-tenant filter applies; the permission gate is the authorization.
/// </summary>
public sealed record ListTenantsQuery(int Limit, int Offset) : IQuery<TenantsResponse>;

public sealed record TenantsResponse(
    IReadOnlyList<TenantItem> Items,
    int Total,
    int Limit,
    int Offset);

public sealed record TenantItem(
    Guid TenantId,
    string Subdomain,
    string Name,
    string Status,
    string Placement,
    DateTimeOffset CreatedAt);
