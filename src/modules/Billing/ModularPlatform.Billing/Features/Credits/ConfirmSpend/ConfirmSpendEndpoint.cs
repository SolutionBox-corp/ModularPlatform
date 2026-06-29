using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Credits.ConfirmSpend;

internal static class ConfirmSpendEndpoint
{
    public static void MapConfirmSpend(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/credits/reservations/confirm", async (
                ConfirmSpendRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new ConfirmSpendCommand(userId, request.ReservationId), ct);
                return Results.Ok(ApiResponse<ConfirmSpendResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("ConfirmSpend");
    }
}
