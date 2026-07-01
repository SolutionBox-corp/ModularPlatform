using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.UpdateTenant;

internal static class UpdateTenantEndpoint
{
    public static void MapUpdateTenant(this IEndpointRouteBuilder app)
    {
        app.MapPut("/tenant/admin/tenants/{tenantId:guid}", async (
                Guid tenantId,
                UpdateTenantRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new UpdateTenantCommand(tenantId, request.Name, request.Subdomain), ct);
                return Results.Ok(ApiResponse<UpdateTenantResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("UpdateTenant");
    }
}
