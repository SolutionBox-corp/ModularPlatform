using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Persistence;

namespace ModularPlatform.Marketing.Features.TenantSnapshots.UpsertTenantSnapshot;

/// <summary>
/// Idempotently updates Marketing's local tenant projection from a public Tenancy fact. Older facts are ignored.
/// </summary>
internal sealed record UpsertTenantSnapshotCommand(
    Guid TenantId,
    string Subdomain,
    string Name,
    Guid SourceEventId,
    DateTimeOffset SourceUpdatedAt) : ICommand;

internal sealed class UpsertTenantSnapshotHandler(MarketingDbContext db)
    : ICommandHandler<UpsertTenantSnapshotCommand>
{
    public async Task<Unit> Handle(UpsertTenantSnapshotCommand command, CancellationToken ct)
    {
        var snapshot = await db.MarketingTenantSnapshots
            .FirstOrDefaultAsync(s => s.TenantId == command.TenantId, ct);

        if (snapshot is not null && snapshot.SourceUpdatedAt >= command.SourceUpdatedAt)
        {
            return Unit.Value;
        }

        if (snapshot is null)
        {
            snapshot = new MarketingTenantSnapshot
            {
                TenantId = command.TenantId,
            };
            db.MarketingTenantSnapshots.Add(snapshot);
        }

        snapshot.Subdomain = command.Subdomain.Trim().ToLowerInvariant();
        snapshot.Name = command.Name.Trim();
        snapshot.SourceEventId = command.SourceEventId;
        snapshot.SourceUpdatedAt = command.SourceUpdatedAt;
        snapshot.SchemaVersion = 1;

        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
