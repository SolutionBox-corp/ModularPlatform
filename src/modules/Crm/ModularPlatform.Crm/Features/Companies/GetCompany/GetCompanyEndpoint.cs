using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Companies;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Companies.GetCompany;

internal static class GetCompanyEndpoint
{
    public static void MapGetCompany(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/companies/{companyId:guid}", async (
                Guid companyId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetCompanyQuery(userId, companyId), ct);
                return Results.Ok(ApiResponse<CompanyResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("GetCompany");
    }
}
