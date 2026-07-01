using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.SetEntitlement;

/// <summary>
/// Platform-admin: toggle (or tier) a tenant's module entitlement. Effective on the tenant's NEXT request (the
/// guard reads it live — never a JWT claim). Operates on the registry by explicit <see cref="TenantId"/>; the
/// registry is not tenant-scoped, so this is a cross-tenant control-plane write gated by <c>platform.tenants.manage</c>.
/// </summary>
public sealed record SetEntitlementCommand(Guid TenantId, string ModuleKey, bool Enabled, string? Tier, string? Limits)
    : ICommand<SetEntitlementResponse>;

public sealed record SetEntitlementResponse(Guid TenantId, string ModuleKey, bool Enabled, string? Tier, string? Limits);

public sealed record SetEntitlementRequest(bool Enabled, string? Tier, string? Limits);
