using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Deals.CreateDeal;

internal static class CreateDealEndpoint
{
    public static void MapCreateDeal(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/deals", async (
                CreateDealRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new CreateDealCommand(
                        userId,
                        request.ContactId,
                        request.CompanyId,
                        request.Title ?? string.Empty,
                        request.AmountCents,
                        string.IsNullOrWhiteSpace(request.Currency) ? "USD" : request.Currency.Trim().ToUpperInvariant(),
                        string.IsNullOrWhiteSpace(request.Stage) ? DealStages.Lead : request.Stage.Trim().ToLowerInvariant(),
                        request.ProbabilityPercent,
                        request.LeadSource is null ? null : request.LeadSource.Trim().ToLowerInvariant(),
                        request.ExpectedCloseAt,
                        request.NextStep,
                        request.Notes),
                    ct);
                var location = links.GetPathByName(http, "GetDeal", new { dealId = result.Id })
                    ?? $"/crm/deals/{result.Id}";
                return Results.Created(location, ApiResponse<CreateDealResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CreateDeal");
    }
}
