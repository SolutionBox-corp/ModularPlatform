using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Subscriptions.CreateSubscriptionCheckout;

internal static class CreateSubscriptionCheckoutEndpoint
{
    public static void MapCreateSubscriptionCheckout(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/subscriptions/checkout", async (
                CreateSubscriptionCheckoutRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new CreateSubscriptionCheckoutCommand(userId, request.PlanKey), ct);
                return Results.Ok(ApiResponse<CreateSubscriptionCheckoutResponse>.Ok(result));
            })
            .RequireAuthorization()
            .DenyMachinePrincipals()
            .WithTags("Billing")
            .WithName("CreateSubscriptionCheckout");
    }
}
