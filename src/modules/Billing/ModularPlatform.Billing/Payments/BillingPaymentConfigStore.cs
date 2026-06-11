using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Payments;
using ModularPlatform.Persistence;

namespace ModularPlatform.Billing.Payments;

/// <summary>
/// Billing's <see cref="IPaymentConfigStore"/> — reads a tenant's <c>payment_configurations</c> row and reveals its
/// credentials from <c>tenant_secrets</c> via <see cref="ISecretProtector"/>, returning a fully-resolved (decrypted)
/// config for the resolver. Runs in BOTH the request context (tenant configuring its own gateway) and the SYSTEM
/// Worker (webhook processing for an arbitrary tenant), so it filters by the EXPLICIT tenant id — never the ambient one.
/// </summary>
internal sealed class BillingPaymentConfigStore(
    IReadDbContextFactory<BillingDbContext> readFactory,
    ISecretProtector secretProtector,
    IConfiguration configuration) : IPaymentConfigStore
{
    public PaymentPlane Plane => PaymentPlane.Tenant;

    public async Task<ResolvedPaymentConfig?> GetAsync(Guid tenantId, PaymentPlane plane, CancellationToken ct = default)
    {
        await using var db = readFactory.Create();

        var config = await db.PaymentConfigurations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Plane == plane, ct);
        if (config is null)
        {
            return null;
        }

        var active = config.Status == Entities.PaymentConfigStatus.Active;

        return config.Provider switch
        {
            PaymentProvider.Fake => new ResolvedPaymentConfig(PaymentProvider.Fake, config.Currency, active),

            PaymentProvider.Stripe => new ResolvedPaymentConfig(
                PaymentProvider.Stripe, config.Currency, active,
                Stripe: new StripeConfig(
                    await RevealAsync(db, tenantId, "stripe.api_key", ct)
                        ?? throw new InvalidOperationException("Stripe API key missing for tenant."),
                    await RevealAsync(db, tenantId, "stripe.webhook_secret", ct))),

            PaymentProvider.GoPay => new ResolvedPaymentConfig(
                PaymentProvider.GoPay, config.Currency, active,
                GoPay: new GoPayCredentials(
                    config.GoPayGoid ?? throw new InvalidOperationException("GoPay goid missing for tenant."),
                    await RevealAsync(db, tenantId, "gopay.client_id", ct)
                        ?? throw new InvalidOperationException("GoPay client id missing for tenant."),
                    await RevealAsync(db, tenantId, "gopay.client_secret", ct)
                        ?? throw new InvalidOperationException("GoPay client secret missing for tenant."),
                    config.Sandbox ? "https://gw.sandbox.gopay.com/api" : "https://gate.gopay.cz/api",
                    BuildNotificationUrl(tenantId, config.WebhookToken))),

            _ => null,
        };
    }

    private async Task<string?> RevealAsync(BillingDbContext db, Guid tenantId, string purpose, CancellationToken ct)
    {
        var secret = await db.TenantSecrets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Purpose == purpose, ct);
        if (secret is null)
        {
            return null;
        }

        return await secretProtector.RevealAsync(
            tenantId, purpose, new ProtectedSecret(secret.KeyVersion, secret.Ciphertext, secret.WrappedDek), ct);
    }

    private string BuildNotificationUrl(Guid tenantId, string? webhookToken)
    {
        var publicBase = (configuration["Billing:Payments:PublicBaseUrl"] ?? "https://localhost").TrimEnd('/');
        return $"{publicBase}/v1/billing/webhooks/gopay/{tenantId:N}/{webhookToken}";
    }
}
