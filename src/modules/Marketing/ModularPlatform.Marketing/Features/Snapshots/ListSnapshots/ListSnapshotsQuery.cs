using ModularPlatform.Cqrs;

namespace ModularPlatform.Marketing.Features.Snapshots.ListSnapshots;

/// <summary>Paged list of the caller's metric snapshots, optionally filtered by source ("ga4", "gsc", …).</summary>
public sealed record ListSnapshotsQuery(Guid UserId, string? Source, PageRequest Page)
    : IQuery<PagedResponse<SnapshotListItem>>;

public sealed record SnapshotListItem(
    Guid Id,
    string Source,
    string MetricName,
    string? Dimension,
    double Value,
    string? DetailJson,
    DateTimeOffset RecordedAt);
