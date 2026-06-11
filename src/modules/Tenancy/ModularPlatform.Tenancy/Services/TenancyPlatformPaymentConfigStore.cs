using Microsoft.Extensions.Configuration;
using ModularPlatform.Payments;

namespace ModularPlatform.Tenancy.Services;

/// <summary>
/// Platform-plane <see cref="IPaymentConfigStore"/> — the PLATFORM's OWN gateway used to charge tenants for the SaaS
/// (tied to their entitlement tier). Reads the platform's single configuration from <c>Platform:Payments:*</c>
/// (the platform's account, NOT a tenant's). Returns null when unconfigured ⇒ the resolver reports the platform plane
/// as not-yet-available, which is correct until the operator configures it. Coexists with Billing's tenant-plane store
/// (each declares its <see cref="Plane"/>; the resolver picks by plane).
/// </summary>
internal sealed class TenancyPlatformPaymentConfigStore(IConfiguration configuration) : IPaymentConfigStore
{
    public PaymentPlane Plane => PaymentPlane.Platform;

    public Task<ResolvedPaymentConfig?> GetAsync(Guid tenantId, PaymentPlane plane, CancellationToken ct = default)
    {
        var section = configuration.GetSection("Platform:Payments");
        var providerName = section["Provider"];
        if (string.IsNullOrWhiteSpace(providerName)
            || !Enum.TryParse<PaymentProvider>(providerName, ignoreCase: true, out var provider))
        {
            return Task.FromResult<ResolvedPaymentConfig?>(null);
        }

        var currency = section["Currency"] ?? "EUR";

        ResolvedPaymentConfig? config = provider switch
        {
            PaymentProvider.Stripe when !string.IsNullOrWhiteSpace(section["StripeApiKey"]) =>
                new ResolvedPaymentConfig(PaymentProvider.Stripe, currency, Active: true,
                    Stripe: new StripeConfig(section["StripeApiKey"]!, section["StripeWebhookSecret"])),
            PaymentProvider.Fake =>
                new ResolvedPaymentConfig(PaymentProvider.Fake, currency, Active: true),
            _ => null,
        };

        return Task.FromResult(config);
    }
}
