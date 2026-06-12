using Stripe;
using Stripe.Checkout;

namespace ModularPlatform.Payments;

/// <summary>
/// Stripe adapter for the neutral <see cref="IPaymentGateway"/>, over Stripe.net. Built with a specific API key +
/// webhook secret (the per-tenant resolver constructs one per tenant). One-off checkout uses an INLINE price (so the
/// neutral port needs no Stripe Price catalogue). Webhook verification is real HMAC + a re-fetch of authoritative
/// state. Subscription-mode checkout needs a recurring price/interval the neutral request doesn't yet carry — wired
/// with the per-tenant plan catalogue (FÁZE 2D); until then it fails fast rather than guessing an interval.
/// </summary>
public sealed class StripePaymentGateway(string apiKey, string? webhookSecret) : IPaymentGateway
{
    private readonly StripeClient _client = new(apiKey);

    public GatewayCapabilities Capabilities { get; } = new(
        SignedWebhooks: true, NativeSubscriptions: true, NativeCoupons: true, NativeTax: true, PreAuthorization: true);

    public async Task<CheckoutResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct = default)
    {
        if (request.Mode == CheckoutMode.Subscription)
        {
            throw new NotSupportedException(
                "Stripe subscription checkout requires a recurring price — wired with the per-tenant plan catalogue (FÁZE 2D).");
        }

        var options = new SessionCreateOptions
        {
            Mode = "payment",
            ClientReferenceId = request.ReferenceId,
            Metadata = request.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
            SuccessUrl = request.SuccessUrl,
            CancelUrl = request.CancelUrl,
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Quantity = 1,
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = request.Currency.ToLowerInvariant(),
                        UnitAmount = request.AmountMinorUnits,
                        ProductData = new SessionLineItemPriceDataProductDataOptions { Name = request.Description },
                    },
                },
            ],
        };

        var session = await new SessionService(_client).CreateAsync(options, cancellationToken: ct);
        return new CheckoutResult(session.Id, session.Url);
    }

    public async Task<PaymentSnapshot> GetPaymentStateAsync(string providerPaymentId, CancellationToken ct = default)
    {
        var session = await new SessionService(_client).GetAsync(providerPaymentId, cancellationToken: ct);
        return ToSnapshot(session);
    }

    public async Task<RefundResult> RefundAsync(string providerPaymentId, long? amountMinorUnits, CancellationToken ct = default)
    {
        var session = await new SessionService(_client).GetAsync(providerPaymentId, cancellationToken: ct);
        if (string.IsNullOrEmpty(session.PaymentIntentId))
        {
            throw new InvalidOperationException($"Stripe session '{providerPaymentId}' has no payment to refund.");
        }

        var refund = await new RefundService(_client).CreateAsync(
            new RefundCreateOptions { PaymentIntent = session.PaymentIntentId, Amount = amountMinorUnits },
            cancellationToken: ct);

        // Total unknown (null on some session kinds) ⇒ can't prove a full refund, so label it partial (conservative).
        var full = amountMinorUnits is null || amountMinorUnits >= (session.AmountTotal ?? long.MaxValue);
        return new RefundResult(refund.Id, full ? PaymentState.Refunded : PaymentState.PartiallyRefunded);
    }

    public async Task<PaymentSnapshot> VerifyNotificationAsync(NotificationContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(webhookSecret))
        {
            throw new InvalidOperationException("Stripe webhook secret is not configured for this gateway.");
        }

        // Real signature verification — throws StripeException on a bad/forged signature.
        var stripeEvent = EventUtility.ConstructEvent(context.RawBody, context.SignatureHeader, webhookSecret);

        // Then re-fetch authoritative state (never trust the payload) when the event carries a checkout session.
        if (stripeEvent.Data.Object is Session session)
        {
            return await GetPaymentStateAsync(session.Id, ct);
        }

        return new PaymentSnapshot(stripeEvent.Id, PaymentState.Pending, null, null,
            new Dictionary<string, string> { ["stripe_event_type"] = stripeEvent.Type });
    }

    public async Task<bool> ValidateCredentialsAsync(CancellationToken ct = default)
    {
        try
        {
            await new BalanceService(_client).GetAsync(cancellationToken: ct);
            return true;
        }
        // A probe: a bad key throws StripeException, but transport faults throw HttpRequestException — both mean
        // "credentials unusable right now". Cancellation propagates, not swallowed.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private static PaymentSnapshot ToSnapshot(Session session) => new(
        session.Id,
        PaymentStateMapping.FromStripe(session.PaymentStatus ?? session.Status),
        session.AmountTotal,
        session.Currency?.ToUpperInvariant(),
        session.Metadata ?? new Dictionary<string, string>());
}
