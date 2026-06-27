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
                int? limit,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new ListInteractionsQuery(userId, contactId, limit ?? 100), ct);
                return Results.Ok(ApiResponse<IReadOnlyList<InteractionResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListInteractions");
    }
}
