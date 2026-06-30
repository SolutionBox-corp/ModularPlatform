using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Contacts.ListInteractions;

internal static class ListInteractionsEndpoint
{
    public static void MapListInteractions(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/contacts/{contactId:guid}/interactions", async (
                Guid contactId,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new ListInteractionsQuery(userId, contactId, null, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<InteractionResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListInteractions");

        app.MapGet("/crm/deals/{dealId:guid}/interactions", async (
                Guid dealId,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new ListInteractionsQuery(userId, null, dealId, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<InteractionResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListDealInteractions");
    }
}
