using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Entitlements.GetMyEntitlements;

/// <summary>
/// "What can my workspace do?" — the SINGLE source for the FE nav. Returns the current tenant's per-module
/// entitlements + tier (advisory: the <c>ModuleEntitlementGuard</c> is the real enforcement; a nav item whose
/// endpoint is still called when not entitled 404s). The tenant comes from the token, never a route/body id.
/// </summary>
public sealed record GetMyEntitlementsQuery : IQuery<TenantEntitlementsView>;
