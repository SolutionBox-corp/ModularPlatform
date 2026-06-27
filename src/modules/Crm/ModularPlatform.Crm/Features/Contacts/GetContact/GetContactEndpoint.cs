using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Contacts.GetContact;

internal static class GetContactEndpoint
{
    public static void MapGetContact(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/contacts/{contactId:guid}", async (
                Guid contactId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetContactQuery(userId, contactId), ct);
                return Results.Ok(ApiResponse<ContactResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("GetContact");
    }
}
