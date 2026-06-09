using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Identity.Features.Admin.AssignRole;

/// <summary>
/// CANONICAL admin endpoint: gated by a fine-grained permission via <c>.RequirePermission(...)</c>. Any module's
/// admin endpoints follow this shape — authenticate, then require the permission; the token's claims decide access.
/// </summary>
internal static class AssignRoleEndpoint
{
    public static void MapAssignRole(this IEndpointRouteBuilder app)
    {
        app.MapPost("/identity/admin/users/{userId:guid}/roles", async (
                Guid userId,
                AssignRoleRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new AssignRoleCommand(userId, request.Role), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequirePermission(PlatformPermissions.IdentityManageRoles)
            .WithTags("Identity")
            .WithName("AssignRole");
    }
}
