using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Contacts;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Contacts.ListContacts;

internal static class ListContactsEndpoint
{
    public static void MapListContacts(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/contacts", async (
                string? status,
                string? company,
                string? email,
                int? limit,
                int? offset,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListContactsQuery(userId, status, company, email, limit ?? 50, offset ?? 0), ct);
                return Results.Ok(ApiResponse<ContactsPageResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListContacts");
    }
}
