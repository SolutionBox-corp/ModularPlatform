using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Companies;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Companies.ListCompanies;

internal static class ListCompaniesEndpoint
{
    public static void MapListCompanies(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/companies", async (
                string? industry,
                string? type,
                string? name,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new ListCompaniesQuery(userId, industry, type, name, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<CompanyListItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListCompanies");
    }
}
