using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Deals.ListDeals;

internal static class ListDealsEndpoint
{
    public static void MapListDeals(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/deals", async (
                string? stage,
                Guid? contactId,
                Guid? companyId,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListDealsQuery(userId, stage, contactId, companyId, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<DealListItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListDeals");
    }
}
