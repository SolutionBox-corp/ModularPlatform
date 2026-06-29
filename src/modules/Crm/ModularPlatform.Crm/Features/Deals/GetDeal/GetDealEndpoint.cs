using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Deals.GetDeal;

internal static class GetDealEndpoint
{
    public static void MapGetDeal(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/deals/{dealId:guid}", async (
                Guid dealId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetDealQuery(userId, dealId), ct);
                return Results.Ok(ApiResponse<DealResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("GetDeal");
    }
}
