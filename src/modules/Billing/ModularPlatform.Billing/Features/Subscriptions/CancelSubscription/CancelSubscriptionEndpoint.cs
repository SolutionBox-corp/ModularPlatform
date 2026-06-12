using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Subscriptions.CancelSubscription;

internal static class CancelSubscriptionEndpoint
{
    public static void MapCancelSubscription(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/subscriptions/cancel", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new CancelSubscriptionCommand(userId), ct);
                return Results.Ok(ApiResponse<CancelSubscriptionResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("CancelSubscription");
    }
}
