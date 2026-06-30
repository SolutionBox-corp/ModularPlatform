using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Deals.UpdateDeal;

internal static class UpdateDealEndpoint
{
    public static void MapUpdateDeal(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/crm/deals/{dealId:guid}", async (
                Guid dealId,
                UpdateDealRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new UpdateDealCommand(
                        userId, dealId, request.Title, request.AmountCents, request.Currency,
                        request.ProbabilityPercent,
                        request.LeadSource is null ? null : request.LeadSource.Trim().ToLowerInvariant(),
                        request.ExpectedCloseAt,
                        request.NextStep,
                        request.Notes),
                    ct);
                return Results.Ok(ApiResponse<DealResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("UpdateDeal");
    }
}
