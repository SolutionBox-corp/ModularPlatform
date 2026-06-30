using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Dashboard.GetCrmDashboard;

public sealed record GetCrmDashboardQuery(Guid UserId, DateTimeOffset Now)
    : IQuery<CrmDashboardResponse>;

public sealed record CrmDashboardResponse(
    long OpenPipelineAmountCents,
    int OpenDealsCount,
    int WonDealsCount,
    int OverdueDealsCount,
    int OpenTasksCount,
    int OverdueTasksCount,
    IReadOnlyList<PipelineStageSummary> Stages,
    IReadOnlyList<LeadSourceSummary> LeadSources);

public sealed record PipelineStageSummary(
    string Stage,
    int Count,
    long AmountCents);

public sealed record LeadSourceSummary(
    string LeadSource,
    int Count,
    long AmountCents);
