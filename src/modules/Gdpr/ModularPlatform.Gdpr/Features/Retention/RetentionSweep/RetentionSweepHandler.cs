using Microsoft.Extensions.Logging;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Gdpr.Features.Retention.RetentionSweep;

/// <summary>
/// GDPR retention sweep. Shredded <c>subject_keys</c> tombstones (a row whose <c>WrappedDek = null</c> and
/// <c>DeletedAt</c> is set) are RETAINED PERMANENTLY and are NOT purged.
/// <para>
/// The tombstone is the only record that stops <c>PersonalDataProtector.GetOrCreateDek</c> from minting a fresh,
/// readable DEK for an already-erased subject (a later PII write for the dead UserId — e.g. a stray notification or
/// audit write). The previous behaviour hard-deleted tombstones after a 30-day window, which silently reopened the
/// crypto-shred: "an erased key cannot be re-created" held only inside that window. A tombstone is non-PII (a
/// subject GUID + an erasure timestamp), so retaining it indefinitely is GDPR-fine — it is an erasure-proof record,
/// like the audit trail.
/// </para>
/// <para>
/// The sweep therefore deletes nothing today; the command / Quartz job / metric remain as the hook for any future
/// module-owned retention work, but the subject-key tombstones must never be among it.
/// </para>
/// </summary>
internal sealed class RetentionSweepHandler(ILogger<RetentionSweepHandler> logger)
    : ICommandHandler<RetentionSweepCommand, RetentionSweepResponse>
{
    public Task<RetentionSweepResponse> Handle(RetentionSweepCommand command, CancellationToken ct)
    {
        logger.LogInformation(
            "GDPR retention sweep: shredded subject_key tombstones are retained permanently (DEK re-mint guard); nothing purged");

        return Task.FromResult(new RetentionSweepResponse(0));
    }
}
