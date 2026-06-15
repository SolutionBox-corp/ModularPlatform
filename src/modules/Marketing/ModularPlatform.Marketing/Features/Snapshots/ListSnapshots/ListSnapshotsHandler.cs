using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Entities;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Marketing.Features.Snapshots.ListSnapshots;

/// <summary>
/// Paged list of the caller's metric snapshots, newest first. Owner-scoped by the explicit <c>WHERE UserId</c> and by
/// RLS. An unknown <c>Source</c> filter yields an empty page rather than an error.
/// </summary>
internal sealed class ListSnapshotsHandler(IReadDbContextFactory<MarketingDbContext> readDb)
    : IQueryHandler<ListSnapshotsQuery, PagedResponse<SnapshotListItem>>
{
    public async Task<PagedResponse<SnapshotListItem>> Handle(ListSnapshotsQuery query, CancellationToken ct)
    {
        await using var db = readDb.Create();

        var snapshots = db.MetricSnapshots.Where(s => s.UserId == query.UserId);

        if (!string.IsNullOrWhiteSpace(query.Source))
        {
            if (!Enum.TryParse<PullSource>(query.Source, ignoreCase: true, out var source))
            {
                return new PagedResponse<SnapshotListItem>([], query.Page.Page, query.Page.PageSize, 0);
            }

            snapshots = snapshots.Where(s => s.Source == source);
        }

        return await snapshots
            .OrderByDescending(s => s.RecordedAt)
            .Select(s => new SnapshotListItem(
                s.Id, s.Source.ToString(), s.MetricName, s.Dimension, s.Value, s.DetailJson, s.RecordedAt))
            .ToPagedResponseAsync(query.Page, ct);
    }
}
