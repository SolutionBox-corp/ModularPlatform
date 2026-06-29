using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Deals;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Deals.MoveDealStage;

internal static class MoveDealStageEndpoint
{
    public static void MapMoveDealStage(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/deals/{dealId:guid}/stage", async (
                Guid dealId,
                MoveDealStageRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new MoveDealStageCommand(userId, dealId, (request.Stage ?? string.Empty).Trim().ToLowerInvariant()), ct);
                return Results.Ok(ApiResponse<DealResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("MoveDealStage");
    }
}
