using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;
using ModularPlatform.Web;

namespace ModularPlatform.Marketing.Features.Analyses.GetAnalysis;

/// <summary>Reads one analysis for the caller who owns it. Owner-scoped by the explicit <c>UserId</c> predicate and RLS.</summary>
internal sealed class GetAnalysisHandler(IReadDbContextFactory<MarketingDbContext> readDb)
    : IQueryHandler<GetAnalysisQuery, AnalysisDetail>
{
    public async Task<AnalysisDetail> Handle(GetAnalysisQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var analysis = await db.MarketingAnalyses
            .Where(a => a.Id == query.AnalysisId && a.UserId == query.UserId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("marketing.analysis_not_found", "Analysis not found.");

        return new AnalysisDetail(
            analysis.Id, analysis.Source.ToString(), analysis.Summary, analysis.InsightsJson,
            analysis.DataPullId, analysis.AnalyzedAt);
    }
}
