using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Persistence;
using ModularPlatform.Telemetry;

namespace ModularPlatform.Gdpr.Features.Retention.RetentionSweep;

/// <summary>
/// Hard-deletes shredded <c>subject_keys</c> tombstones beyond the retention window.
/// <para>
/// A tombstone is a <see cref="Entities.SubjectKey"/> row whose <c>WrappedDek = null</c> and
/// <c>DeletedAt</c> is set (crypto-shredded by the erasure flow). Keeping tombstones for a grace period
/// (default 30 days) lets audit queries confirm erasure. After that window they have no further value and
/// are purged to keep the table tidy.
/// </para>
/// <para>
/// <c>ExecuteDelete</c> is used intentionally: these are tombstone rows whose deletion does not need to be
/// audited (the audit trail from the shred is already written; the final purge is operational hygiene).
/// Per CLAUDE.md §4, <c>ExecuteDelete</c> bypasses the audit interceptor — acceptable here.
/// </para>
/// </summary>
internal sealed class RetentionSweepHandler(
    GdprDbContext db,
    IConfiguration configuration,
    IClock clock,
    ILogger<RetentionSweepHandler> logger)
    : ICommandHandler<RetentionSweepCommand, RetentionSweepResponse>
{
    private static readonly System.Diagnostics.Metrics.Counter<long> SweptCounter =
        PlatformMetrics.Meter.CreateCounter<long>(
            "platform.gdpr.retention_swept",
            description: "Number of shredded subject_key tombstones purged by the retention sweep.");

    public async Task<RetentionSweepResponse> Handle(RetentionSweepCommand command, CancellationToken ct)
    {
        var retentionDays = configuration.GetValue<int>("Gdpr:Retention:ShreddedKeyRetentionDays", 30);
        var cutoff = clock.UtcNow.AddDays(-retentionDays);

        // Hard-delete tombstones whose DeletedAt is beyond the retention window.
        // ExecuteDelete is correct here — see XML doc; no audit interception needed for final purge.
        var purged = await db.SubjectKeys
            .Where(k => k.DeletedAt != null && k.DeletedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (purged > 0)
        {
            SweptCounter.Add(purged);
            logger.LogInformation(
                "GDPR retention sweep: purged {Count} shredded subject_key tombstone(s) older than {Days} days",
                purged, retentionDays);
        }
        else
        {
            logger.LogInformation(
                "GDPR retention sweep: no subject_key tombstones older than {Days} days found", retentionDays);
        }

        return new RetentionSweepResponse(purged);
    }
}
