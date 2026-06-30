using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Dashboard.GetCrmDashboard;

internal sealed class GetCrmDashboardHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<GetCrmDashboardQuery, CrmDashboardResponse>
{
    public async Task<CrmDashboardResponse> Handle(GetCrmDashboardQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var terminalStages = new[] { DealStages.Won, DealStages.Lost };
        var openDeals = db.Deals.Where(d => d.UserId == query.UserId && !terminalStages.Contains(d.Stage));
        var openTasks = db.Tasks.Where(t => t.UserId == query.UserId && t.Status == TaskStatuses.Open);

        var stages = await db.Deals
            .Where(d => d.UserId == query.UserId)
            .GroupBy(d => d.Stage)
            .Select(g => new PipelineStageSummary(g.Key, g.Count(), g.Sum(d => d.AmountCents)))
            .ToListAsync(ct);

        var leadSourceRows = await db.Deals
            .Where(d => d.UserId == query.UserId && d.LeadSource != null)
            .GroupBy(d => d.LeadSource!)
            .Select(g => new { LeadSource = g.Key, Count = g.Count(), AmountCents = g.Sum(d => d.AmountCents) })
            .OrderByDescending(row => row.AmountCents)
            .Take(8)
            .ToListAsync(ct);
        var leadSources = leadSourceRows
            .Select(row => new LeadSourceSummary(row.LeadSource, row.Count, row.AmountCents))
            .ToList();

        var openPipelineAmount = await openDeals.Select(d => (long?)d.AmountCents).SumAsync(ct) ?? 0L;
        var openDealsCount = await openDeals.CountAsync(ct);
        var wonDealsCount = await db.Deals.CountAsync(d => d.UserId == query.UserId && d.Stage == DealStages.Won, ct);
        var overdueDealsCount = await openDeals.CountAsync(d => d.ExpectedCloseAt != null && d.ExpectedCloseAt < query.Now, ct);
        var openTasksCount = await openTasks.CountAsync(ct);
        var overdueTasksCount = await openTasks.CountAsync(t => t.DueAt != null && t.DueAt < query.Now, ct);

        return new CrmDashboardResponse(
            openPipelineAmount,
            openDealsCount,
            wonDealsCount,
            overdueDealsCount,
            openTasksCount,
            overdueTasksCount,
            stages,
            leadSources);
    }
}
