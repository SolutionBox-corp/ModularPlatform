using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.ListTenantInvites;

public sealed record ListTenantInvitesQuery(Guid TenantId, int Limit, int Offset, string? Status)
    : IQuery<TenantInvitesResponse>;

public sealed record TenantInvitesResponse(
    IReadOnlyList<TenantInviteItem> Items,
    int Total,
    int Limit,
    int Offset);

public sealed record TenantInviteItem(
    Guid InviteId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt,
    DateTimeOffset? RevokedAt);
