using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Subscriptions.GetMySubscription;

internal static class GetMySubscriptionEndpoint
{
    public static void MapGetMySubscription(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/subscriptions/me", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var subscription = await dispatcher.Query(new GetMySubscriptionQuery(userId), ct);
                return Results.Ok(ApiResponse<SubscriptionResponse>.Ok(subscription));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("GetMySubscription");
    }
}
