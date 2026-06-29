using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Kanban;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.GetBoard;

internal static class GetBoardEndpoint
{
    public static void MapGetBoard(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/boards/{boardId:guid}", async (
                Guid boardId, ITenantContext tenant, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetBoardQuery(userId, boardId), ct);
                return Results.Ok(ApiResponse<KanbanBoardDetail>.Ok(result));
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("GetBoard");
    }
}
