using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Analyses.GetAnalysis;

public sealed record GetAnalysisQuery(Guid AnalysisId, Guid UserId) : IQuery<AnalysisDetail>;

public sealed record AnalysisDetail(
    Guid Id,
    string Source,
    string Summary,
    string? InsightsJson,
    Guid? DataPullId,
    DateTimeOffset AnalyzedAt);
