using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.CreateTenantInvite;

/// <summary>
/// Platform-admin mints a single-use invite for a tenant (so its first/next member can join an <c>InviteOnly</c>
/// workspace). <c>TenantId</c> comes from the route (a platform-admin operation, like SetEntitlement); the raw token
/// is returned ONCE (only its hash is stored).
/// </summary>
public sealed record CreateTenantInviteCommand(Guid TenantId, int ExpiresInDays) : ICommand<CreateTenantInviteResponse>;

public sealed record CreateTenantInviteResponse(string InviteToken, DateTimeOffset ExpiresAt);

public sealed record CreateTenantInviteRequest(int? ExpiresInDays);
