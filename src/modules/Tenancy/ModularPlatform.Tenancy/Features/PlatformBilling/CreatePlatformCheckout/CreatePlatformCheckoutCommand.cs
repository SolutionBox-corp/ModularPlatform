using ModularPlatform.Cqrs;

namespace ModularPlatform.Tenancy.Features.PlatformBilling.CreatePlatformCheckout;

/// <summary>
/// Platform-plane checkout: the TENANT pays the SaaS operator (for its plan/entitlement tier) via the PLATFORM's own
/// gateway. Resolves <c>PaymentPlane.Platform</c> through the shared resolver and returns the redirect URL. The tenant
/// comes from the token (Law 10); the money flows to the PLATFORM's account, not a tenant's. Amount is in minor units.
/// </summary>
public sealed record CreatePlatformCheckoutCommand(long AmountMinorUnits, string Currency, string Description)
    : ICommand<CreatePlatformCheckoutResponse>;

public sealed record CreatePlatformCheckoutResponse(string ProviderPaymentId, string RedirectUrl);

public sealed record CreatePlatformCheckoutRequest(long AmountMinorUnits, string Currency, string Description);
