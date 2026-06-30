using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.UnlockUser;

internal static class UnlockUserEndpoint
{
    public static void MapUnlockUser(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/admin/users/{userId:guid}/unlock", async (
                Guid userId,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new UnlockUserCommand(userId), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequirePermission(PlatformPermissions.IdentityManageRoles)
            .WithTags("Identity")
            .WithName("UnlockUser");
    }
}
