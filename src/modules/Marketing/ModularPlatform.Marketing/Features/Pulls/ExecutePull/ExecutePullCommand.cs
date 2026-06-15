using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Integrations;
using ModularPlatform.Marketing.Persistence;

namespace ModularPlatform.Marketing.Features.Pulls.ExecutePull;

/// <summary>Internal work command for a marketing pull (dispatched by the durable Worker shell, not exposed over HTTP).</summary>
internal sealed record ExecutePullCommand(Guid DataPullId) : ICommand;

/// <summary>
/// Does the actual pull: transitions Pending → Running, calls the source's gateway (the external HTTP call), persists
/// the raw payload + normalized snapshots, then → Completed. Any failure drives the pull to a TERMINAL Failed state so
/// the caller never polls a stuck pull (mirrors the Operations worker). Runs under system context (RLS bypassed).
/// </summary>
internal sealed class ExecutePullHandler(
    MarketingDbContext db,
    IGa4Gateway ga4,
    IGscGateway gsc,
    IClock clock,
    ILogger<ExecutePullHandler> logger)
    : ICommandHandler<ExecutePullCommand>
{
    public async Task<Unit> Handle(ExecutePullCommand command, CancellationToken ct)
    {
        var pull = await db.DataPulls.FirstOrDefaultAsync(p => p.Id == command.DataPullId, ct);
        if (pull is null)
        {
            logger.LogWarning("Data pull {DataPullId} not found; skipping.", command.DataPullId);
            return Unit.Value;
        }

        // Terminal pulls are final — a redelivered message must not re-run a completed/failed pull.
        if (pull.Status is PullStatus.Completed or PullStatus.Failed)
        {
            return Unit.Value;
        }

        try
        {
            pull.Status = PullStatus.Running;
            await db.SaveChangesAsync(ct);

            var window = JsonSerializer.Deserialize<PullWindow>(pull.ParamsJson ?? "{}") ?? PullWindow.LastMonth(clock);
            var result = await ExecuteAsync(pull, window, ct);

            pull.RawResultJson = result.RawJson;
            foreach (var row in result.Metrics)
            {
                db.MetricSnapshots.Add(new MetricSnapshot
                {
                    UserId = pull.UserId,
                    DataPullId = pull.Id,
                    Source = pull.Source,
                    MetricName = row.MetricName,
                    Dimension = row.Dimension,
                    Value = row.Value,
                    DetailJson = row.DetailJson,
                    RecordedAt = clock.UtcNow,
                });
            }

            pull.Status = PullStatus.Completed;
            pull.CompletedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch (NotSupportedException ex)
        {
            logger.LogWarning(ex, "Pull source {Source} not yet supported (pull {DataPullId}).", pull.Source, pull.Id);
            await FailAsync(pull, "marketing.source_not_supported", ct);
        }
        catch (Exception ex)
        {
            // Drive to a TERMINAL state on any failure so the caller never polls a stuck pull. The detail surfaced via
            // the status endpoint is a generic error code; the real exception is logged server-side.
            logger.LogError(ex, "Data pull {DataPullId} ({Source}) failed.", pull.Id, pull.Source);
            await FailAsync(pull, "marketing.pull_failed", ct);
        }

        return Unit.Value;
    }

    private async Task<PullResult> ExecuteAsync(DataPull pull, PullWindow window, CancellationToken ct) =>
        pull.Source switch
        {
            PullSource.Ga4 => await ga4.RunReportAsync(pull.UserId, new Ga4PullParams(window.Start, window.End), ct),
            PullSource.Gsc => await gsc.SearchAnalyticsAsync(pull.UserId, new GscPullParams(window.Start, window.End), ct),
            _ => throw new NotSupportedException($"Pull source {pull.Source} is not wired yet."),
        };

    private async Task FailAsync(DataPull pull, string errorCode, CancellationToken ct)
    {
        pull.Status = PullStatus.Failed;
        pull.ErrorCode = errorCode;
        pull.CompletedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private sealed record PullWindow(DateOnly Start, DateOnly End)
    {
        public static PullWindow LastMonth(IClock clock)
        {
            var end = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
            return new PullWindow(end.AddDays(-27), end);
        }
    }
}
