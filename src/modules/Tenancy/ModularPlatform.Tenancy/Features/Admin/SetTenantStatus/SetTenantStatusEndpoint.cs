using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.SetTenantStatus;

internal static class SetTenantStatusEndpoint
{
    public static void MapSetTenantStatus(this IEndpointRouteBuilder app)
    {
        app.MapPut("/tenant/admin/tenants/{tenantId:guid}/status", async (
                Guid tenantId,
                SetTenantStatusRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new SetTenantStatusCommand(tenantId, request.Status), ct);
                return Results.Ok(ApiResponse<SetTenantStatusResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("SetTenantStatus");
    }
}
