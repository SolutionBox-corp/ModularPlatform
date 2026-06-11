using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.PlatformBilling.CreatePlatformCheckout;

internal static class CreatePlatformCheckoutEndpoint
{
    public static void MapCreatePlatformCheckout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tenant/me/platform-checkout", async (
                CreatePlatformCheckoutRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new CreatePlatformCheckoutCommand(
                    request.AmountMinorUnits, request.Currency, request.Description), ct);
                return Results.Ok(ApiResponse<CreatePlatformCheckoutResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Tenancy")
            .WithName("CreatePlatformCheckout");
    }
}
