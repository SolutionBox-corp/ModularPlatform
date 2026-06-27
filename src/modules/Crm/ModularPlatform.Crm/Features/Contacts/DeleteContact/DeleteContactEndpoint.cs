using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Contacts.DeleteContact;

internal static class DeleteContactEndpoint
{
    public static void MapDeleteContact(this IEndpointRouteBuilder app)
    {
        app.MapDelete("/crm/contacts/{contactId:guid}", async (
                Guid contactId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new DeleteContactCommand(userId, contactId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("DeleteContact");
    }
}
