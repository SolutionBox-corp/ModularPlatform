using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Analyses.ListAnalyses;

/// <summary>Paged list of the caller's AI analyses (owner from the token; RLS-scoped).</summary>
internal static class ListAnalysesEndpoint
{
    public static void MapListAnalyses(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marketing/analyses", async (
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListAnalysesQuery(userId, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<AnalysisListItem>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("ListAnalyses");
    }
}
