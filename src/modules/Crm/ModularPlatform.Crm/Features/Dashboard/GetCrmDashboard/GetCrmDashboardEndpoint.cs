using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Dashboard.GetCrmDashboard;

internal static class GetCrmDashboardEndpoint
{
    public static void MapGetCrmDashboard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/dashboard", async (
                ITenantContext tenant,
                IClock clock,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetCrmDashboardQuery(userId, clock.UtcNow), ct);
                return Results.Ok(ApiResponse<CrmDashboardResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("GetCrmDashboard");
    }
}
