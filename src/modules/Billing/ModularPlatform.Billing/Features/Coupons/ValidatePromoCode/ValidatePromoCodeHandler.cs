using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Coupons.ValidatePromoCode;

/// <summary>
/// Pre-checkout promo-code validation. Coupons/promotions live ENTIRELY in Stripe (discount math, redemption
/// limits, expiry) — this read just lets the UI confirm a code before redirecting to Checkout, where Stripe
/// enforces it authoritatively (<c>AllowPromotionCodes</c>).
/// </summary>
internal sealed class ValidatePromoCodeHandler(IStripeGateway gateway)
    : IQueryHandler<ValidatePromoCodeQuery, PromoCodeResponse>
{
    public async Task<PromoCodeResponse> Handle(ValidatePromoCodeQuery query, CancellationToken ct)
    {
        var promo = await gateway.FindActivePromotionCodeAsync(query.Code, ct)
            ?? throw new NotFoundException("billing.coupon.invalid", "Unknown or inactive promotion code.");

        return new PromoCodeResponse(promo.Code, promo.PercentOff, promo.AmountOff, promo.Currency);
    }
}
