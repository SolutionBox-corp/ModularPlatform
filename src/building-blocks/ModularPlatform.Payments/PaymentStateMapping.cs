namespace ModularPlatform.Payments;

/// <summary>
/// Normalizes each provider's raw status string into the neutral <see cref="PaymentState"/>. Kept as pure, testable
/// logic the adapters call — the single place provider vocabulary is translated, so a status the provider adds later
/// is handled in ONE spot. Unknown/empty ⇒ <see cref="PaymentState.Pending"/> (never silently "Paid").
/// </summary>
public static class PaymentStateMapping
{
    /// <summary>Maps a Stripe PaymentIntent / Checkout-Session status. (verified against Stripe's status set)</summary>
    public static PaymentState FromStripe(string? status) => status switch
    {
        "succeeded" or "complete" or "paid" or "no_payment_required" => PaymentState.Paid,
        "requires_capture" => PaymentState.Authorized,
        "canceled" => PaymentState.Canceled,
        "expired" => PaymentState.Expired,
        "processing" or "requires_action" or "requires_confirmation" or "requires_payment_method"
            or "open" or "unpaid" => PaymentState.Pending,
        _ => PaymentState.Pending,
    };

    /// <summary>Maps a GoPay payment <c>state</c>. (verified against GoPay's PaymentStatus set)</summary>
    public static PaymentState FromGoPay(string? state) => state switch
    {
        "PAID" => PaymentState.Paid,
        "AUTHORIZED" => PaymentState.Authorized,
        "CREATED" => PaymentState.Created,
        "PAYMENT_METHOD_CHOSEN" => PaymentState.Pending,
        "CANCELED" => PaymentState.Canceled,
        "TIMEOUTED" => PaymentState.Expired,
        "REFUNDED" => PaymentState.Refunded,
        "PARTIALLY_REFUNDED" => PaymentState.PartiallyRefunded,
        _ => PaymentState.Pending,
    };
}
