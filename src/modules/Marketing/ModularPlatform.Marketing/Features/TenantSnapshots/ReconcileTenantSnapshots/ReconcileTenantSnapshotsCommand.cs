using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.TenantSnapshots.UpsertTenantSnapshot;
using ModularPlatform.Marketing.Persistence;

namespace ModularPlatform.Marketing.Features.TenantSnapshots.ReconcileTenantSnapshots;

/// <summary>
/// Repairs Marketing's tenant projections from the public Tenancy directory port. No Tenancy Core reference or join.
/// </summary>
internal sealed record ReconcileTenantSnapshotsCommand(Guid? TenantId = null)
    : ICommand<ReconcileTenantSnapshotsResponse>;

internal sealed record ReconcileTenantSnapshotsResponse(int Scanned, int Repaired, int Missing);

internal sealed class ReconcileTenantSnapshotsHandler(
    MarketingDbContext db,
    ITenantDirectory tenants,
    IClock clock,
    IDispatcher dispatcher)
    : ICommandHandler<ReconcileTenantSnapshotsCommand, ReconcileTenantSnapshotsResponse>
{
    public async Task<ReconcileTenantSnapshotsResponse> Handle(ReconcileTenantSnapshotsCommand command, CancellationToken ct)
    {
        var tenantIds = command.TenantId is { } tenantId
            ? new List<Guid> { tenantId }
            : await db.MarketingTenantSnapshots
                .IgnoreQueryFilters()
                .Select(s => s.TenantId)
                .Distinct()
                .ToListAsync(ct);

        var repaired = 0;
        var missing = 0;
        foreach (var id in tenantIds.Distinct())
        {
            var info = await tenants.GetByIdAsync(id, ct);
            if (info is null)
            {
                missing++;
                continue;
            }

            await dispatcher.Send(new UpsertTenantSnapshotCommand(
                info.Id,
                info.Subdomain,
                info.Name,
                SourceEventId: Guid.Empty,
                SourceUpdatedAt: clock.UtcNow), ct);
            repaired++;
        }

        return new ReconcileTenantSnapshotsResponse(tenantIds.Count, repaired, missing);
    }
}
