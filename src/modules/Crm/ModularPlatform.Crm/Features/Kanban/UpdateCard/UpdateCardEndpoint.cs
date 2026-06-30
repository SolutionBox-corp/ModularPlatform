using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Kanban;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.UpdateCard;

internal static class UpdateCardEndpoint
{
    public static void MapUpdateCard(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/crm/cards/{cardId:guid}", async (
                Guid cardId,
                UpdateCardRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new UpdateCardCommand(
                    userId,
                    cardId,
                    request.Title,
                    request.Description,
                    request.ContactId,
                    request.DealId,
                    request.MeetingId,
                    request.TaskId,
                    request.AssigneeUserId,
                    request.Priority?.Trim().ToLowerInvariant(),
                    request.Labels,
                    request.StartAt,
                    request.DueAt), ct);
                return Results.Ok(ApiResponse<KanbanCardDto>.Ok(result));
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("UpdateCard");
    }
}
