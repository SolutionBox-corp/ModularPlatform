using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using ModularPlatform.Payments;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.PaymentGateway.ConfigureGateway;

internal sealed class ConfigureGatewayHandler(
    IDbContextOutbox<BillingDbContext> outbox,
    ISecretProtector secretProtector,
    ITenantContext tenant,
    IHostEnvironment environment,
    HttpClient http,
    GoPayTokenCache goPayTokenCache,
    IClock clock) : ICommandHandler<ConfigureGatewayCommand, ConfigureGatewayResponse>
{
    public async Task<ConfigureGatewayResponse> Handle(ConfigureGatewayCommand command, CancellationToken ct)
    {
        var tenantId = tenant.TenantId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        if (!Enum.TryParse<PaymentProvider>(command.Provider, ignoreCase: true, out var provider))
        {
            throw new BusinessRuleException("billing.gateway.unknown_provider", "Unknown payment provider.");
        }

        // The Fake gateway accepts every payment — a TEST-ONLY seam. A tenant must never select it in Production
        // (it would let them bypass real payment collection entirely).
        if (provider == PaymentProvider.Fake && environment.IsProduction())
        {
            throw new BusinessRuleException(
                "billing.gateway.fake_not_allowed", "The fake payment gateway is not allowed in production.");
        }

        // Verify the submitted credentials BEFORE activating, so a wrong key fails at config time with a clear error
        // instead of an opaque provider exception on the first real checkout/webhook. Gated to Production: the call hits
        // the real provider (Stripe Balance / GoPay OAuth), which the test/dev fake keys can't satisfy.
        if (environment.IsProduction() && !await ValidateSubmittedCredentialsAsync(provider, command, ct))
        {
            throw new BusinessRuleException(
                "billing.gateway.credentials_invalid", "The payment gateway credentials could not be validated.");
        }

        var db = outbox.DbContext;

        var config = await db.PaymentConfigurations
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Plane == PaymentPlane.Tenant, ct);
        if (config is null)
        {
            config = new PaymentConfiguration { TenantId = tenantId, Plane = PaymentPlane.Tenant, CreatedAt = clock.UtcNow };
            db.PaymentConfigurations.Add(config);
        }

        config.Provider = provider;
        config.Currency = command.Currency.Trim().ToUpperInvariant();
        config.Sandbox = command.Sandbox;
        config.GoPayGoid = command.GoPayGoid;
        config.WebhookToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        config.Status = PaymentConfigStatus.Active;

        switch (provider)
        {
            case PaymentProvider.Stripe:
                await SealAsync(db, tenantId, "stripe.api_key",
                    Require(command.StripeApiKey, "billing.gateway.stripe_key_required"), ct);
                // REQUIRED: without it the tenant webhook can't verify signatures and would 500 on every Stripe retry
                // (an un-acknowledged 5xx loops forever, blocking all credit grants for that tenant). Fail fast here.
                await SealAsync(db, tenantId, "stripe.webhook_secret",
                    Require(command.StripeWebhookSecret, "billing.gateway.stripe_webhook_secret_required"), ct);
                break;

            case PaymentProvider.GoPay:
                _ = command.GoPayGoid ?? throw new BusinessRuleException("billing.gateway.gopay_goid_required", "GoPay goid is required.");
                await SealAsync(db, tenantId, "gopay.client_id",
                    Require(command.GoPayClientId, "billing.gateway.gopay_client_required"), ct);
                await SealAsync(db, tenantId, "gopay.client_secret",
                    Require(command.GoPayClientSecret, "billing.gateway.gopay_client_required"), ct);
                break;

            case PaymentProvider.Fake:
                break;
        }

        await outbox.SaveChangesAndFlushMessagesAsync();
        return new ConfigureGatewayResponse(config.Id, provider.ToString(), config.Status == PaymentConfigStatus.Active);
    }

    private async Task SealAsync(BillingDbContext db, Guid tenantId, string purpose, string plaintext, CancellationToken ct)
    {
        var sealedSecret = await secretProtector.ProtectAsync(tenantId, purpose, plaintext, ct);

        var secret = await db.TenantSecrets.FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Purpose == purpose, ct);
        if (secret is null)
        {
            secret = new TenantSecret { TenantId = tenantId, Purpose = purpose, CreatedAt = clock.UtcNow };
            db.TenantSecrets.Add(secret);
        }

        secret.KeyVersion = sealedSecret.KeyVersion;
        secret.Ciphertext = sealedSecret.Ciphertext;
        secret.WrappedDek = sealedSecret.WrappedDek;
    }

    private static string Require(string? value, string errorCode) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new BusinessRuleException(errorCode, "Required gateway credential is missing.")
            : value;

    /// <summary>Builds a throwaway gateway from the SUBMITTED plaintext creds and probes the provider.</summary>
    private async Task<bool> ValidateSubmittedCredentialsAsync(
        PaymentProvider provider, ConfigureGatewayCommand command, CancellationToken ct)
    {
        IPaymentGateway probe = provider switch
        {
            PaymentProvider.Stripe => new StripePaymentGateway(
                Require(command.StripeApiKey, "billing.gateway.stripe_key_required"), command.StripeWebhookSecret),
            PaymentProvider.GoPay => new GoPayPaymentGateway(
                http,
                new GoPayCredentials(
                    command.GoPayGoid ?? throw new BusinessRuleException("billing.gateway.gopay_goid_required", "GoPay goid is required."),
                    Require(command.GoPayClientId, "billing.gateway.gopay_client_required"),
                    Require(command.GoPayClientSecret, "billing.gateway.gopay_client_required"),
                    command.Sandbox ? "https://gw.sandbox.gopay.com/api" : "https://gate.gopay.cz/api",
                    NotificationUrl: string.Empty),
                clock,
                goPayTokenCache),
            _ => throw new BusinessRuleException("billing.gateway.unknown_provider", "Unknown payment provider."),
        };

        return await probe.ValidateCredentialsAsync(ct);
    }
}
