using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.ListTenants;

/// <summary>Platform-admin cross-tenant registry listing. Gated by <c>platform.tenants.manage</c>.</summary>
internal static class ListTenantsEndpoint
{
    public static void MapListTenants(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenant/admin/tenants", async (
                int? limit,
                int? offset,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Query(new ListTenantsQuery(limit ?? 50, offset ?? 0), ct);
                return Results.Ok(ApiResponse<TenantsResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("ListTenants");
    }
}
