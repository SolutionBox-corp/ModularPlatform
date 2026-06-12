using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Credits.ReleaseHold;

internal static class ReleaseHoldEndpoint
{
    public static void MapReleaseHold(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/credits/reservations/release", async (
                ReleaseHoldRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new ReleaseHoldCommand(userId, request.ReservationId), ct);
                return Results.Ok(ApiResponse<ReleaseHoldResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("ReleaseHold");
    }
}
