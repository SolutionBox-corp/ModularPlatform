using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.PlatformBilling.CreatePlatformCheckout;

/// <summary>
/// Platform-plane checkout: the TENANT pays the SaaS operator (for its plan/entitlement tier) via the PLATFORM's own
/// gateway. Resolves <c>PaymentPlane.Platform</c> through the shared resolver and returns the redirect URL. The tenant
/// comes from the token (Law 10); the money flows to the PLATFORM's account, not a tenant's. The price is
/// SERVER-AUTHORITATIVE: the caller picks a plan KEY and the amount/currency come from <c>Platform:Payments:Plans</c>
/// config (never a client-supplied amount — that would let a tenant pay €0.01 for a plan).
/// </summary>
public sealed record CreatePlatformCheckoutCommand(string PlanKey)
    : ICommand<CreatePlatformCheckoutResponse>;

public sealed record CreatePlatformCheckoutResponse(string ProviderPaymentId, string RedirectUrl);

public sealed record CreatePlatformCheckoutRequest(string PlanKey);
