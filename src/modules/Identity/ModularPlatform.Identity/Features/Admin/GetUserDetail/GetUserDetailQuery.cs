using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.GetUserDetail;

/// <summary>
/// Admin (CROSS-USER) read: one user's profile plus their CURRENT role names, for the role manager. The target
/// user id is a ROUTE id (an admin operation over another subject, like role assignment / audit trail) - the
/// permission, not the token subject, is the authorization.
/// </summary>
public sealed record GetUserDetailQuery(Guid UserId) : IQuery<UserDetailResponse>;

/// <summary>
/// One user's admin detail. <c>Email</c>/<c>DisplayName</c> are [Encrypted] columns - the read model converter
/// decrypts them automatically on projection; a shredded subject surfaces as <c>[erased]</c>. <c>Roles</c> are the
/// names currently assigned via <c>user_roles</c>. <c>IsLocked</c> reflects an active lockout window.
/// </summary>
public sealed record UserDetailResponse(
    Guid Id,
    string Email,
    string? DisplayName,
    IReadOnlyList<string> Roles,
    bool IsLocked,
    DateTimeOffset CreatedAt);
