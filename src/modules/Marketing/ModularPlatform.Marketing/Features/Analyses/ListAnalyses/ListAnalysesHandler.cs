using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Marketing.Features.Analyses.ListAnalyses;

/// <summary>Paged list of the caller's analyses, newest first. Owner-scoped by the explicit <c>WHERE UserId</c> and RLS.</summary>
internal sealed class ListAnalysesHandler(IReadDbContextFactory<MarketingDbContext> readDb)
    : IQueryHandler<ListAnalysesQuery, PagedResponse<AnalysisListItem>>
{
    public async Task<PagedResponse<AnalysisListItem>> Handle(ListAnalysesQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        return await db.MarketingAnalyses
            .Where(a => a.UserId == query.UserId)
            .OrderByDescending(a => a.AnalyzedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new AnalysisListItem(a.Id, a.Source.ToString(), a.Summary, a.AnalyzedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
