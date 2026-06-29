using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.MoveCard;

internal static class MoveCardEndpoint
{
    public static void MapMoveCard(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/cards/{cardId:guid}/move", async (
                Guid cardId, MoveCardRequest request, ITenantContext tenant, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new MoveCardCommand(userId, cardId, request.ColumnId, request.Position), ct);
                return Results.NoContent();
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("MoveCard");
    }
}
