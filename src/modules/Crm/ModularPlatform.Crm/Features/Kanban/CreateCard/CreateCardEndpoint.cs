using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.CreateCard;

internal static class CreateCardEndpoint
{
    public static void MapCreateCard(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/boards/{boardId:guid}/cards", async (
                Guid boardId, CreateCardRequest request, ITenantContext tenant, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new CreateCardCommand(
                    userId, boardId, request.ColumnId, request.Title ?? string.Empty, request.Description,
                    request.ContactId, request.DealId, request.MeetingId, request.TaskId, request.AssigneeUserId,
                    request.Priority?.Trim().ToLowerInvariant(), request.Labels, request.StartAt, request.DueAt), ct);
                return Results.Created((string?)null, ApiResponse<CreateCardResponse>.Ok(result));
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("CreateCard");
    }
}
