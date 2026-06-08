using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Credits.ReserveCredits;

internal static class ReserveCreditsEndpoint
{
    public static void MapReserveCredits(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/credits/reservations", async (
                ReserveCreditsRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new ReserveCreditsCommand(userId, request.Amount, request.HoldMinutes), ct);
                return Results.Ok(ApiResponse<ReserveCreditsResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Billing")
            .WithName("ReserveCredits");
    }
}
