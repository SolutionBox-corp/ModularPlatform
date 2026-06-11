using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.Admin.GetPlatformBillingStatus;

/// <summary>
/// Platform-plane billing status for the CURRENT tenant — the tenant paying the SaaS operator for its entitlement
/// tier. This is the read-only SEAM: it surfaces the tenant's current tier as the platform-plane "plan" so the
/// admin UI can show what the tenant is on. The actual charging flow (Checkout via the PLATFORM gateway through
/// <c>IPaymentGatewayResolver</c> with <c>PaymentPlane.Platform</c>) is a later step. The tenant comes from the
/// token (<see cref="ModularPlatform.Abstractions.ITenantContext.TenantId"/>), never a route/body id.
/// </summary>
public sealed record GetPlatformBillingStatusQuery : IQuery<PlatformBillingStatusView>;

/// <summary>
/// The platform-plane billing snapshot: which tenant, its current plan (entitlement tier — <c>"free"</c> when the
/// tenant has no explicit tier yet), and the modules that make up that tier. <c>Plan</c> is the platform-plane
/// subscription the tenant is billed for.
/// </summary>
public sealed record PlatformBillingStatusView(
    Guid TenantId,
    string Plan,
    IReadOnlyList<PlatformBillingModuleView> Modules);

/// <summary>One entitled module contributing to the tenant's platform-plane plan.</summary>
public sealed record PlatformBillingModuleView(string Key, bool Enabled, string? Tier);
