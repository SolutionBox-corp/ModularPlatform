using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.ProvisionTenant;

internal static class ProvisionTenantEndpoint
{
    public static void MapProvisionTenant(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tenant/admin/tenants", async (
                ProvisionTenantRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new ProvisionTenantCommand(request.Name, request.Subdomain), ct);
                return Results.Ok(ApiResponse<ProvisionTenantResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("ProvisionTenant");
    }
}
