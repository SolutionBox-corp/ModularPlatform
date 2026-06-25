using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.GetUserDetail;

/// <summary>
/// Admin read of one user's profile + current roles for the role manager. Gated by <c>identity.manage_roles</c>,
/// identical to its sibling <c>AssignRole</c> - the target user id is a ROUTE id (an admin operation over another
/// subject); the permission, not the token subject, is the authorization.
/// </summary>
internal static class GetUserDetailEndpoint
{
    public static void MapGetUserDetail(this IEndpointRouteBuilder app)
    {
        app.MapGet("/identity/admin/users/{userId:guid}", async (
                Guid userId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var detail = await dispatcher.Query(new GetUserDetailQuery(userId), ct);
                return Results.Ok(ApiResponse<UserDetailResponse>.Ok(detail));
            })
            .RequirePermission(PlatformPermissions.IdentityManageRoles)
            .WithTags("Identity")
            .WithName("GetUserDetail");
    }
}
