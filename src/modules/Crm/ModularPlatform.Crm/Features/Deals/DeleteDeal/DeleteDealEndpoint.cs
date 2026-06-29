using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Deals.DeleteDeal;

internal static class DeleteDealEndpoint
{
    public static void MapDeleteDeal(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/crm/deals/{dealId:guid}", async (
                Guid dealId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new DeleteDealCommand(userId, dealId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("DeleteDeal");
    }
}
