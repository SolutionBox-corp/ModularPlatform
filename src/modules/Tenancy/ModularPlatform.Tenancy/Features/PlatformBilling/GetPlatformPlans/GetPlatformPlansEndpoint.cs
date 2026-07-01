using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.PlatformBilling.GetPlatformPlans;

internal static class GetPlatformPlansEndpoint
{
    public static void MapGetPlatformPlans(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenant/me/platform-plans", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var plans = await dispatcher.Query(new GetPlatformPlansQuery(), ct);
                return Results.Ok(ApiResponse<IReadOnlyList<PlatformPlanResponse>>.Ok(plans));
            })
            .RequireAuthorization()
            .DenyMachinePrincipals()
            .WithTags("Tenancy")
            .WithName("GetPlatformPlans");
    }
}
