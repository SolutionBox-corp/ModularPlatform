using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.RevokeTenantInvite;

public sealed record RevokeTenantInviteCommand(Guid TenantId, Guid InviteId) : ICommand<RevokeTenantInviteResponse>;

public sealed record RevokeTenantInviteResponse(
    Guid InviteId,
    string Status,
    DateTimeOffset? RevokedAt);
