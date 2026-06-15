using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Analyses.ListAnalyses;

/// <summary>Paged list of the caller's AI analyses, newest first.</summary>
public sealed record ListAnalysesQuery(Guid UserId, PageRequest Page) : IQuery<PagedResponse<AnalysisListItem>>;

public sealed record AnalysisListItem(
    Guid Id,
    string Source,
    string Summary,
    DateTimeOffset AnalyzedAt);
