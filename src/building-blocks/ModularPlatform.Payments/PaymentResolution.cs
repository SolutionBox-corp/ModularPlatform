using ModularPlatform.Abstractions;

namespace ModularPlatform.Payments;

/// <summary>Which money flow: the platform charging a tenant for the SaaS, or a tenant charging its own end-users.</summary>
public enum PaymentPlane
{
    /// <summary>Tenant → SaaS operator. Uses the PLATFORM's own gateway (one account). Tied to the entitlement tier.</summary>
    Platform,

    /// <summary>End-user → tenant. Uses the TENANT's own gateway/credentials. The money never touches the platform.</summary>
    Tenant,
}

/// <summary>The payment provider a config selects.</summary>
public enum PaymentProvider
{
    Stripe,
    GoPay,
    Fake,
}

/// <summary>Stripe credentials for one (tenant, plane). Plaintext — resolved just-in-time; NEVER log/serialize.</summary>
public sealed record StripeConfig(string ApiKey, string? WebhookSecret);

/// <summary>
/// A fully-resolved, DECRYPTED payment configuration for one <c>(tenant, plane)</c>. The owning module's
/// <see cref="IPaymentConfigStore"/> reads its <c>PaymentConfiguration</c> row + reveals the secrets via
/// <c>ISecretProtector</c> and returns this; the resolver only picks the adapter. Plaintext credentials live here for
/// the lifetime of one resolve — never persist, log, or put them on an event/outbox envelope.
/// </summary>
public sealed record ResolvedPaymentConfig(
    PaymentProvider Provider,
    string Currency,
    bool Active,
    StripeConfig? Stripe = null,
    GoPayCredentials? GoPay = null);

/// <summary>
/// Port the resolver reads per-tenant gateway config through. Implemented by the module that OWNS the
/// <c>PaymentConfiguration</c> + <c>tenant_secrets</c> (Billing for the tenant plane; Tenancy for the platform plane),
/// which does the secret decryption. Keeps the building-block free of any module entity.
/// </summary>
public interface IPaymentConfigStore
{
    /// <summary>Which plane this store serves. The resolver picks the store whose plane matches, so the tenant-plane (Billing) and platform-plane (Tenancy) stores coexist in one container.</summary>
    PaymentPlane Plane { get; }

    Task<ResolvedPaymentConfig?> GetAsync(Guid tenantId, PaymentPlane plane, CancellationToken ct = default);
}

/// <summary>
/// Hands back the <see cref="IPaymentGateway"/> bound to the right credentials for a <c>(tenant, plane)</c>. A missing
/// or inactive config is a hard failure (the caller maps it) — never silently fall back to another tenant's gateway.
/// </summary>
public interface IPaymentGatewayResolver
{
    Task<IPaymentGateway> ResolveAsync(Guid tenantId, PaymentPlane plane, CancellationToken ct = default);
}

internal sealed class PaymentGatewayResolver(
    IEnumerable<IPaymentConfigStore> stores, HttpClient http, IClock clock,
    FakePaymentGateway? sharedFake = null, GoPayTokenCache? goPayTokenCache = null)
    : IPaymentGatewayResolver
{
    // Shared OAuth-token cache across the throwaway per-request GoPay gateways (a fresh one is fine for unit tests).
    private readonly GoPayTokenCache _goPayTokenCache = goPayTokenCache ?? new GoPayTokenCache();

    public async Task<IPaymentGateway> ResolveAsync(Guid tenantId, PaymentPlane plane, CancellationToken ct = default)
    {
        // Pick the store that OWNS this plane (Billing = Tenant, Tenancy = Platform); both coexist as IEnumerable.
        var store = stores.FirstOrDefault(s => s.Plane == plane)
            ?? throw new PaymentGatewayUnavailableException("payment.gateway_not_configured",
                "No payment gateway is configured for this workspace.");

        var config = await store.GetAsync(tenantId, plane, ct)
            ?? throw new PaymentGatewayUnavailableException("payment.gateway_not_configured",
                "No payment gateway is configured for this workspace.");

        if (!config.Active)
        {
            throw new PaymentGatewayUnavailableException("payment.gateway_inactive",
                "The payment gateway for this workspace is not active.");
        }

        return config.Provider switch
        {
            PaymentProvider.Stripe => new StripePaymentGateway(
                config.Stripe?.ApiKey ?? throw MissingCredentials(), config.Stripe.WebhookSecret),
            PaymentProvider.GoPay => new GoPayPaymentGateway(
                http, config.GoPay ?? throw MissingCredentials(), clock, _goPayTokenCache),
            // A DI-registered shared fake (test harness) lets a checkout created on one request be re-fetched by the
            // webhook on another; without one, a throwaway per-resolve instance is used.
            PaymentProvider.Fake => sharedFake ?? new FakePaymentGateway(),
            _ => throw new PaymentGatewayUnavailableException("payment.gateway_not_configured", "Unknown payment provider."),
        };

        static PaymentGatewayUnavailableException MissingCredentials() =>
            new("payment.gateway_not_configured", "The payment gateway credentials are incomplete.");
    }
}

/// <summary>A (tenant, plane) has no usable gateway. Carries a stable error code the web layer maps (e.g. to 422/409).</summary>
public sealed class PaymentGatewayUnavailableException(string errorCode, string message) : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
