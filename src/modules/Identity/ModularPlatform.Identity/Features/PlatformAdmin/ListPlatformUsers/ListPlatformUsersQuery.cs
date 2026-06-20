using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.PlatformAdmin.ListPlatformUsers;

/// <summary>
/// Platform-admin (CROSS-TENANT) read: list users across ALL tenants, optionally narrowed to one tenant.
/// Bypasses the per-tenant query filter (re-adding only the soft-delete guard). Paged.
/// </summary>
public sealed record ListPlatformUsersQuery(Guid? TenantId, int Limit, int Offset)
    : IQuery<PlatformUsersResponse>;

public sealed record PlatformUsersResponse(
    IReadOnlyList<PlatformUserItem> Items,
    int Total,
    int Limit,
    int Offset);

/// <summary>
/// One user row. <c>Email</c>/<c>DisplayName</c> are [Encrypted] columns — the read model converter decrypts
/// them automatically on projection; a shredded subject surfaces as <c>[erased]</c>.
/// </summary>
public sealed record PlatformUserItem(
    Guid UserId,
    string Email,
    string? DisplayName,
    Guid? TenantId,
    DateTimeOffset CreatedAt);
