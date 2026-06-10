using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Features.Coupons.ValidatePromoCode;

public sealed record ValidatePromoCodeQuery(string Code) : IQuery<PromoCodeResponse>;

public sealed record PromoCodeResponse(string Code, decimal? PercentOff, long? AmountOff, string? Currency);
