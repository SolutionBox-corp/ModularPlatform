using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.GetPlatformBillingStatus;

internal static class GetPlatformBillingStatusEndpoint
{
    public static void MapGetPlatformBillingStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenant/admin/platform-billing", async (IDispatcher dispatcher, CancellationToken ct) =>
            {
                var status = await dispatcher.Query(new GetPlatformBillingStatusQuery(), ct);
                return Results.Ok(ApiResponse<PlatformBillingStatusView>.Ok(status));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("GetPlatformBillingStatus");
    }
}
