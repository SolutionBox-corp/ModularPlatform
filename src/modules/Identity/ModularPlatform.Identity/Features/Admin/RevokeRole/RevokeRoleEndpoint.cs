using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.RevokeRole;

internal static class RevokeRoleEndpoint
{
    public static void MapRevokeRole(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/identity/admin/users/{userId:guid}/roles/{role}", async (
                Guid userId,
                string role,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new RevokeRoleCommand(userId, role), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequirePermission(PlatformPermissions.IdentityManageRoles)
            .WithTags("Identity")
            .WithName("RevokeRole");
    }
}
