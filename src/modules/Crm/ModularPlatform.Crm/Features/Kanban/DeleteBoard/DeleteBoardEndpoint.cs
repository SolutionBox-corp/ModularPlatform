using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.DeleteBoard;

internal static class DeleteBoardEndpoint
{
    public static void MapDeleteBoard(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/crm/boards/{boardId:guid}", async (
                Guid boardId, ITenantContext tenant, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new DeleteBoardCommand(userId, boardId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("DeleteBoard");
    }
}
