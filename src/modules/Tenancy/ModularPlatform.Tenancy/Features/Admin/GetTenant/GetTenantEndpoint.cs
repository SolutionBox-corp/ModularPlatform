using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.GetTenant;

/// <summary>Platform-admin cross-tenant detail (registry row + entitlements). Gated by <c>platform.tenants.manage</c>.</summary>
internal static class GetTenantEndpoint
{
    public static void MapGetTenant(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenant/admin/tenants/{tenantId:guid}", async (
                Guid tenantId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Query(new GetTenantQuery(tenantId), ct);
                return Results.Ok(ApiResponse<TenantDetail>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("GetTenant");
    }
}
