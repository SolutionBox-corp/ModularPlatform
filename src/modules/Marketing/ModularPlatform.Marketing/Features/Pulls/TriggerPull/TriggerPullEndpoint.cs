using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Pulls.TriggerPull;

/// <summary>
/// 202 endpoint: accepts a pull, kicks off the durable Worker job, returns <c>202 Accepted</c> + a <c>Location</c>
/// to the status endpoint. Owner is taken from the token. The pull window defaults to the last 28 days (UTC).
/// </summary>
internal static class TriggerPullEndpoint
{
    public static void MapTriggerPull(this IEndpointRouteBuilder app)
    {
        app.MapPost("/marketing/pulls", async (
                TriggerPullRequest request,
                ITenantContext tenant,
                IClock clock,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");

                var endDate = request.EndDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
                var startDate = request.StartDate ?? endDate.AddDays(-27);

                var result = await dispatcher.Send(
                    new TriggerPullCommand(userId, request.Source, startDate, endDate), ct);

                var location = links.GetPathByName(http, "GetPullStatus", new { dataPullId = result.DataPullId })
                    ?? $"/marketing/pulls/{result.DataPullId}";
                return Results.Accepted(location, ApiResponse<TriggerPullResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("TriggerPull");
    }
}
