using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Retention.RetentionSweep;

/// <summary>
/// Retention sweep command, dispatched by the Jobs host on a nightly cron (default 03:00 UTC).
/// <para>
/// IMPORTANT: shredded <c>subject_keys</c> tombstones are RETAINED PERMANENTLY — they are the DEK re-mint guard.
/// <see cref="ModularPlatform.Gdpr.Security.PersonalDataProtector"/> mints a fresh (readable) DEK for a subject
/// ONLY when no row exists; deleting a tombstone would let a post-erasure PII write resurrect a readable key and
/// silently un-erase the subject. So this sweep deliberately purges NOTHING in the current design and returns 0.
/// The command + cron are kept as the seam for any future module-owned, genuinely-purgeable retention data (the
/// pattern to copy), and <c>Gdpr:Retention:ShreddedKeyRetentionDays</c> remains configurable for that.
/// </para>
/// </summary>
internal sealed record RetentionSweepCommand : ICommand<RetentionSweepResponse>;

/// <summary>Number of tombstones purged in a single sweep run.</summary>
internal sealed record RetentionSweepResponse(int PurgedCount);
