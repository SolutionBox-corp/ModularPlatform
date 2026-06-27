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
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new CreateContactCommand(
                        userId,
                        request.FullName ?? string.Empty,
                        request.Email,
                        request.Phone,
                        request.Company,
                        request.Position,
                        request.Notes,
                        request.Tags ?? [],
                        string.IsNullOrWhiteSpace(request.Status) ? ContactStatuses.Lead : request.Status.Trim().ToLowerInvariant()),
                    ct);
                // 201 with no fabricated Location: building "/v1/crm/contacts/{id}" by hand would hardcode the
                // version-group prefix (forbidden). The client reads back via GET /crm/contacts/{id} using the id.
                return Results.Created((string?)null, ApiResponse<CreateContactResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CreateContact");
    }
}
