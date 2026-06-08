using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Credits.CreditTopUp;

internal static class CreditTopUpEndpoint
{
    public static void MapCreditTopUp(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/credits/topup", async (
                CreditTopUpRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new CreditTopUpCommand(
                    userId, request.Amount, request.BucketExpiryDays, request.IdempotencyKey), ct);
                return Results.Ok(ApiResponse<CreditTopUpResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Billing")
            .WithName("CreditTopUp");
    }
}
