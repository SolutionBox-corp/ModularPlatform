using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.CreateColumn;

internal static class CreateColumnEndpoint
{
    public static void MapCreateColumn(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/boards/{boardId:guid}/columns", async (
                Guid boardId, CreateColumnRequest request, ITenantContext tenant, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new CreateColumnCommand(userId, boardId, request.Name ?? string.Empty), ct);
                return Results.Created((string?)null, ApiResponse<CreateColumnResponse>.Ok(result));
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("CreateColumn");
    }
}
