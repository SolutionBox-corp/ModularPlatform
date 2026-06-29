using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Contacts.UpdateContact;

internal static class UpdateContactEndpoint
{
    public static void MapUpdateContact(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/crm/contacts/{contactId:guid}", async (
                Guid contactId,
                UpdateContactRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new UpdateContactCommand(
                        userId,
                        contactId,
                        request.CompanyId,
                        request.CompanyId is not null,
                        request.FullName,
                        request.Email,
                        request.Phone,
                        request.Company,
                        request.Position,
                        request.Notes,
                        request.Tags,
                        request.Status is null ? null : request.Status.Trim().ToLowerInvariant()),
                    ct);
                return Results.Ok(ApiResponse<ContactResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("UpdateContact");
    }
}
