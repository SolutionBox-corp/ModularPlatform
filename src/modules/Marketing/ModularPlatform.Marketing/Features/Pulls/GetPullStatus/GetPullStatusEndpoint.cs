using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Pulls.GetPullStatus;

/// <summary>The status-polling half of the 202 pull pattern. Owner-scoped; 404 for anyone but the owner.</summary>
internal static class GetPullStatusEndpoint
{
    public static void MapGetPullStatus(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marketing/pulls/{dataPullId:guid}", async (
                Guid dataPullId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetPullStatusQuery(dataPullId, userId), ct);
                return Results.Ok(ApiResponse<PullStatusResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("GetPullStatus");
    }
}
