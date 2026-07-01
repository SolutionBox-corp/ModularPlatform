using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Tenancy.Features.Admin.ListTenantInvites;

internal static class ListTenantInvitesEndpoint
{
    public static void MapListTenantInvites(this IEndpointRouteBuilder app)
    {
        app.MapGet("/tenant/admin/tenants/{tenantId:guid}/invites", async (
                Guid tenantId,
                int? limit,
                int? offset,
                string? status,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Query(
                    new ListTenantInvitesQuery(tenantId, limit ?? 50, offset ?? 0, status), ct);
                return Results.Ok(ApiResponse<TenantInvitesResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformTenantsManage)
            .WithTags("Tenancy")
            .WithName("ListTenantInvites");
    }
}
