using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.Subscriptions.GetSubscriptionPlans;

internal static class GetSubscriptionPlansEndpoint
{
    public static void MapGetSubscriptionPlans(this IEndpointRouteBuilder app)
    {
        app.MapGet("/billing/subscriptions/plans", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var plans = await dispatcher.Query(new GetSubscriptionPlansQuery(), ct);
                return Results.Ok(ApiResponse<IReadOnlyList<SubscriptionPlanResponse>>.Ok(plans));
            })
            .RequireAuthorization()
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("GetSubscriptionPlans");
    }
}
