using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Purchases.GetCreditPurchase;

internal static class GetCreditPurchaseEndpoint
{
    public static void MapGetCreditPurchase(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/purchases/{purchaseId:guid}", async (
                Guid purchaseId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var purchase = await dispatcher.Query(new GetCreditPurchaseQuery(userId, purchaseId), ct);
                return Results.Ok(ApiResponse<CreditPurchaseResponse>.Ok(purchase));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("GetCreditPurchase");
    }
}
