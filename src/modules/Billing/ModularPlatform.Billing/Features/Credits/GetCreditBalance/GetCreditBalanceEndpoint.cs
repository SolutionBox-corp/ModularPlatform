using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Credits.GetCreditBalance;

internal static class GetCreditBalanceEndpoint
{
    public static void MapGetCreditBalance(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/credits/balance", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var balance = await dispatcher.Query(new GetCreditBalanceQuery(userId), ct);
                return Results.Ok(ApiResponse<CreditBalanceResponse>.Ok(balance));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("GetCreditBalance");
    }
}
