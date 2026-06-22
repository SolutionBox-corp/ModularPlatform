using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.PlatformAdmin.ListPlatformUsers;

/// <summary>
/// Platform-admin CROSS-TENANT user directory. Gated by <c>platform.users.list</c>; cross-tenant scope is the
/// authorization (the permission), not the token subject. Optional <c>tenantId</c> narrows to a single tenant.
/// </summary>
internal static class ListPlatformUsersEndpoint
{
    public static void MapListPlatformUsers(this IEndpointRouteBuilder app)
    {
        app.MapGet("/identity/platform/users", async (
                Guid? tenantId,
                int? limit,
                int? offset,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Query(
                    new ListPlatformUsersQuery(tenantId, limit ?? 50, offset ?? 0), ct);
                return Results.Ok(ApiResponse<PlatformUsersResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.PlatformUsersList)
            .WithTags("Identity")
            .WithName("ListPlatformUsers");
    }
}
