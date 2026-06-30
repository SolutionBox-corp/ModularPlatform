using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.RevokeUserSessions;

internal static class RevokeUserSessionsEndpoint
{
    public static void MapRevokeUserSessions(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/admin/users/{userId:guid}/sessions/revoke", async (
                Guid userId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new RevokeUserSessionsCommand(userId), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequirePermission(PlatformPermissions.IdentityManageRoles)
            .WithTags("Identity")
            .WithName("RevokeUserSessions");
    }
}
