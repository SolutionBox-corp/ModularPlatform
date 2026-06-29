using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.CreateBoard;

internal static class CreateBoardEndpoint
{
    public static void MapCreateBoard(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/boards", async (
                CreateBoardRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new CreateBoardCommand(userId, request.Name ?? string.Empty), ct);
                return Results.Created((string?)null, ApiResponse<CreateBoardResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CreateBoard");
    }
}
