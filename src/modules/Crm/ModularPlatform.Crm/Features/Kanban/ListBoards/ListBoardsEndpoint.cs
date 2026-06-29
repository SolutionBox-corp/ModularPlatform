using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Kanban;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.ListBoards;

internal static class ListBoardsEndpoint
{
    public static void MapListBoards(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/boards", async (
                int? page, int? pageSize, ITenantContext tenant, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new ListBoardsQuery(userId, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<KanbanBoardListItem>>.Ok(result));
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("ListBoards");
    }
}
