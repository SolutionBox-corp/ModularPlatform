using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.PaymentGateway.CreateTenantCheckout;

internal static class CreateTenantCheckoutEndpoint
{
    public static void MapCreateTenantCheckout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/payments/checkout", async (
                CreateTenantCheckoutRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new CreateTenantCheckoutCommand(
                    request.AmountMinorUnits, request.Currency, request.Description), ct);
                return Results.Ok(ApiResponse<CreateTenantCheckoutResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("CreateTenantCheckout");
    }
}
