using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Contacts.AddInteraction;

internal static class AddInteractionEndpoint
{
    public static void MapAddInteraction(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/contacts/{contactId:guid}/interactions", async (
                Guid contactId,
                AddInteractionRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new AddInteractionCommand(
                        userId,
                        contactId,
                        (request.Type ?? string.Empty).Trim().ToLowerInvariant(),
                        request.OccurredAt,
                        request.Body),
                    ct);
                return Results.Created((string?)null, ApiResponse<AddInteractionResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("AddInteraction");
    }
}
