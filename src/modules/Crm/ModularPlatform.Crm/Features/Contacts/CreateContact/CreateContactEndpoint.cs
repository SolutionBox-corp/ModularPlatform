using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Contacts.CreateContact;

internal static class CreateContactEndpoint
{
    public static void MapCreateContact(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/contacts", async (
                CreateContactRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new CreateContactCommand(
                        userId,
                        request.CompanyId,
                        request.FirstName ?? string.Empty,
                        request.LastName ?? string.Empty,
                        request.Email,
                        request.Phone,
                        request.Position,
                        request.Notes,
                        request.Tags ?? [],
                        string.IsNullOrWhiteSpace(request.Status) ? ContactStatuses.New : request.Status.Trim().ToLowerInvariant()),
                    ct);
                var location = links.GetPathByName(http, "GetContact", new { contactId = result.Id })
                    ?? $"/crm/contacts/{result.Id}";
                return Results.Created(location, ApiResponse<CreateContactResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CreateContact");
    }
}
