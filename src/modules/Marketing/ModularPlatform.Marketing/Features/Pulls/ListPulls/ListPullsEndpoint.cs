using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Pulls.GetPullStatus;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Pulls.ListPulls;

/// <summary>Paged list of the caller's data pulls (owner from the token; RLS-scoped).</summary>
internal static class ListPullsEndpoint
{
    public static void MapListPulls(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marketing/pulls", async (
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListPullsQuery(userId, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<PullStatusResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("ListPulls");
    }
}
