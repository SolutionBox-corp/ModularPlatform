using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Analyses.GetAnalysis;

/// <summary>Reads one AI analysis (owner from the token; RLS-scoped; 404 for anyone but the owner).</summary>
internal static class GetAnalysisEndpoint
{
    public static void MapGetAnalysis(this IEndpointRouteBuilder app)
    {
        app.MapGet("/marketing/analyses/{analysisId:guid}", async (
                Guid analysisId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetAnalysisQuery(analysisId, userId), ct);
                return Results.Ok(ApiResponse<AnalysisDetail>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("marketing")
            .WithTags("Marketing")
            .WithName("GetAnalysis");
    }
}
