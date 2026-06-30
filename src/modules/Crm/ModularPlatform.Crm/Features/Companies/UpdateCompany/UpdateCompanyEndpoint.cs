using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Companies;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Companies.UpdateCompany;

internal static class UpdateCompanyEndpoint
{
    public static void MapUpdateCompany(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/crm/companies/{companyId:guid}", async (
                Guid companyId,
                UpdateCompanyRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new UpdateCompanyCommand(
                        userId,
                        companyId,
                        request.Name,
                        request.Domain,
                        request.Industry,
                        request.Type is null ? null : request.Type.Trim().ToLowerInvariant(),
                        request.IdentificationNumber,
                        request.TaxIdentificationNumber,
                        request.RegisteredAddress,
                        request.City,
                        request.PostalCode,
                        request.Country,
                        request.Notes),
                    ct);
                return Results.Ok(ApiResponse<CompanyResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("UpdateCompany");
    }
}
