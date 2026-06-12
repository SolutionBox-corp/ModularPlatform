using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Coupons.ValidatePromoCode;

internal static class ValidatePromoCodeEndpoint
{
    public static void MapValidatePromoCode(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/promo-codes/{code}/validate", async (
                string code,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var promo = await dispatcher.Query(new ValidatePromoCodeQuery(code), ct);
                return Results.Ok(ApiResponse<PromoCodeResponse>.Ok(promo));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("ValidatePromoCode");
    }
}
