using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Companies.CreateCompany;

internal static class CreateCompanyEndpoint
{
    public static void MapCreateCompany(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/companies", async (
                CreateCompanyRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new CreateCompanyCommand(
                        userId,
                        request.Name ?? string.Empty,
                        request.Domain,
                        request.Industry,
                        request.IdentificationNumber,
                        request.TaxIdentificationNumber,
                        request.RegisteredAddress,
                        request.City,
                        request.PostalCode,
                        request.Country,
                        request.Notes),
                    ct);
                var location = links.GetPathByName(http, "GetCompany", new { companyId = result.Id })
                    ?? $"/crm/companies/{result.Id}";
                return Results.Created(location, ApiResponse<CreateCompanyResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CreateCompany");
    }
}
