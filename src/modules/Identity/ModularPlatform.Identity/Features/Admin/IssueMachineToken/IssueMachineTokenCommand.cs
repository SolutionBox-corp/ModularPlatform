using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.IssueMachineToken;

/// <summary>
/// Mints a tenant-scoped MACHINE principal — a non-user JWT for an edge agent / device gateway. It carries the
/// tenant's id (so it is tenant-scoped exactly like a user token, and the tenant-resolution cross-check applies) plus a
/// <c>machine</c> role and NO user identity. This is the seam a future Devices module uses to authenticate agents;
/// long-lived/refreshable machine credentials are a later concern (this issues a standard-lifetime access token).
/// </summary>
public sealed record IssueMachineTokenCommand(Guid TenantId, string Name) : ICommand<IssueMachineTokenResponse>;

public sealed record IssueMachineTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record IssueMachineTokenRequest(Guid TenantId, string Name);
