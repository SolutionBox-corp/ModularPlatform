using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Kanban.DeleteCard;

internal static class DeleteCardEndpoint
{
    public static void MapDeleteCard(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/crm/cards/{cardId:guid}", async (
                Guid cardId, ITenantContext tenant, IDispatcher dispatcher, CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new DeleteCardCommand(userId, cardId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization().RequireModule("crm").WithTags("Crm").WithName("DeleteCard");
    }
}
