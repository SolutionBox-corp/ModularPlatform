using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.PaymentGateway.CreateTenantCheckout;

/// <summary>
/// Creates a one-off checkout on the TENANT's OWN configured gateway (tenant-plane: an end-user paying the tenant).
/// Resolves the per-tenant <c>IPaymentGateway</c> via the resolver (Stripe / GoPay / Fake per the tenant's config) and
/// returns the redirect URL the client opens. The tenant comes from the token (Law 10); the money goes to the tenant's
/// account, never the platform. Amount is in minor units (cents/haléře).
/// </summary>
public sealed record CreateTenantCheckoutCommand(long AmountMinorUnits, string Currency, string Description)
    : ICommand<CreateTenantCheckoutResponse>;

public sealed record CreateTenantCheckoutResponse(string ProviderPaymentId, string RedirectUrl);

public sealed record CreateTenantCheckoutRequest(long AmountMinorUnits, string Currency, string Description);
