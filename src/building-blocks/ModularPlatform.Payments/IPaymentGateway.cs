namespace ModularPlatform.Payments;

/// <summary>
/// The ONE provider-neutral payment seam — the generalization of the old Stripe-only port. Two concrete adapters
/// (<c>StripePaymentGateway</c>, <c>GoPayPaymentGateway</c>) plus an in-memory fake implement it; a per-tenant resolver
/// hands back the instance bound to the right credentials for a <c>(tenant, plane)</c>. Business logic depends only on
/// this interface and the neutral <see cref="PaymentTypes"/> — never on an SDK type or a provider string.
/// <para>
/// Leaky provider differences are surfaced via <see cref="Capabilities"/> (branch, don't pretend parity) and the
/// strategy inside <see cref="VerifyNotificationAsync"/> (signature-verify for Stripe vs re-fetch for GoPay), NOT by
/// widening the common surface. Recurring (mandate + charge), capture/void and other optional capabilities are added
/// behind capability flags as the subscription/pre-auth flows are wired.
/// </para>
/// </summary>
public interface IPaymentGateway
{
    /// <summary>What this provider can do natively, so the host can branch instead of assuming parity.</summary>
    GatewayCapabilities Capabilities { get; }

    /// <summary>Creates a checkout and returns the redirect URL + the provider payment id. App-side idempotency uses <see cref="CheckoutRequest.ReferenceId"/>.</summary>
    Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default);

    /// <summary>Re-fetches the authoritative payment state from the provider (the trust primitive; mandatory for GoPay).</summary>
    Task<PaymentSnapshot> GetPaymentStateAsync(string providerPaymentId, CancellationToken ct = default);

    /// <summary>Refunds all (<paramref name="amountMinorUnits"/> null) or part of a payment.</summary>
    Task<RefundResult> RefundAsync(string providerPaymentId, long? amountMinorUnits, CancellationToken ct = default);

    /// <summary>Returns the authoritative state for a webhook notification — verifying the signature (Stripe) or re-fetching (GoPay). Never trust the raw payload.</summary>
    Task<PaymentSnapshot> VerifyNotificationAsync(NotificationContext context, CancellationToken ct = default);

    /// <summary>A live read-only call proving the configured credentials work — run at onboarding BEFORE activating a tenant's gateway.</summary>
    Task<bool> ValidateCredentialsAsync(CancellationToken ct = default);
}
