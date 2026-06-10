using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Retention.RetentionSweep;

/// <summary>
/// Retention sweep command, dispatched by the Jobs host on a nightly cron (default 03:00 UTC).
/// Hard-deletes <c>subject_keys</c> tombstones whose <c>DeletedAt</c> is older than the configured
/// retention window (<c>Gdpr:Retention:ShreddedKeyRetentionDays</c>, default 30 days).
/// Retaining tombstones for a brief period allows an erasure confirmation audit trail before final purge.
/// </summary>
internal sealed record RetentionSweepCommand : ICommand<RetentionSweepResponse>;

/// <summary>Number of tombstones purged in a single sweep run.</summary>
internal sealed record RetentionSweepResponse(int PurgedCount);
