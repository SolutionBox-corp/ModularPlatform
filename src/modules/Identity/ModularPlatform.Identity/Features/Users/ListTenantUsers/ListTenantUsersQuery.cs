using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Users.ListTenantUsers;

/// <summary>Tenant-scoped user picker for product modules (assignees/collaborators). Tenant comes from the token.</summary>
public sealed record ListTenantUsersQuery(Guid TenantId, int? Page, int? PageSize)
    : IQuery<PagedResponse<TenantUserListItem>>;

public sealed record TenantUserListItem(
    Guid Id,
    string Email,
    string? DisplayName);
