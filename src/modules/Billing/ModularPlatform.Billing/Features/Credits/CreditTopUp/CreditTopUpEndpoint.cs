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
                // Namespace the client-supplied idempotency key so it can NEVER collide with the ledger's structured
                // system keys (purchase:{id}, sub-invoice:{id}, …) in the per-account UNIQUE space — a collision would
                // drop a real grant or absorb a fake one. The prefix is transparent to the client.
                var result = await dispatcher.Send(new CreditTopUpCommand(
                    userId, request.Amount, request.BucketExpiryDays, $"client:{request.IdempotencyKey}"), ct);
                return Results.Ok(ApiResponse<CreditTopUpResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequirePermission(PlatformPermissions.BillingManage)
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("CreditTopUp");
    }
}
