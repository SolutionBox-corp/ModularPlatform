using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Integrations;
using ModularPlatform.Marketing.Persistence;

namespace ModularPlatform.Marketing.Features.Analyses.AnalyzeMarketingData;

/// <summary>Internal command that runs the AI analysis for a completed pull (dispatched by the analysis Worker shell).</summary>
internal sealed record AnalyzeMarketingDataCommand(Guid DataPullId, Guid UserId, string Source) : ICommand;

/// <summary>
/// Loads the snapshots a completed pull produced, asks the AI gateway to analyze them, and persists a
/// <see cref="MarketingAnalysis"/>. Runs under system context on the Worker. A pull with no snapshots is skipped
/// (nothing to analyze).
/// </summary>
internal sealed class AnalyzeMarketingDataHandler(
    MarketingDbContext db,
    IMarketingAiGateway ai,
    IClock clock)
    : ICommandHandler<AnalyzeMarketingDataCommand>
{
    public async Task<Unit> Handle(AnalyzeMarketingDataCommand command, CancellationToken ct)
    {
        var metrics = await db.MetricSnapshots
            .Where(s => s.DataPullId == command.DataPullId)
            .Select(s => new { s.MetricName, s.Dimension, s.Value, s.DetailJson })
            .ToListAsync(ct);

        if (metrics.Count == 0)
        {
            return Unit.Value;
        }

        var metricsJson = JsonSerializer.Serialize(metrics);
        var result = await ai.AnalyzeAsync(command.Source, metricsJson, ct);

        var source = Enum.Parse<PullSource>(command.Source, ignoreCase: true);
        db.MarketingAnalyses.Add(new MarketingAnalysis
        {
            UserId = command.UserId,
            DataPullId = command.DataPullId,
            Source = source,
            Summary = result.Summary,
            InsightsJson = result.InsightsJson,
            AnalyzedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
