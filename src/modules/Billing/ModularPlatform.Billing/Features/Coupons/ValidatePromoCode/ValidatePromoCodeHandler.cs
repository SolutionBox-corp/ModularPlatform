using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using Stripe;

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
        var code = query.Code.Trim();

        PromotionCodeState? promo;
        try
        {
            promo = await gateway.FindActivePromotionCodeAsync(code, ct);
        }
        catch (StripeException ex)
        {
            throw new BusinessRuleException(
                "billing.coupon.provider_failed",
                $"The promo-code provider rejected the request: {ex.Message}");
        }

        if (promo is null)
        {
            throw new NotFoundException("billing.coupon.invalid", "Unknown or inactive promotion code.");
        }

        return new PromoCodeResponse(promo.Code, promo.PercentOff, promo.AmountOff, promo.Currency);
    }
}
