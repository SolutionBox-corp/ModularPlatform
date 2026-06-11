using Microsoft.Extensions.Logging;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Contracts;
using ModularPlatform.Gdpr.Features.Erasure.ShredSubjectKey;

namespace ModularPlatform.Gdpr.Messaging;

/// <summary>
/// In-proc erasure path. Wolverine auto-discovers this handler and the Worker runs it when a
/// <see cref="UserErasureRequested"/> event is published; the inbox dedups it. Two things happen:
/// <list type="number">
/// <item>
/// It fans out across every module's <see cref="IErasePersonalData"/> implementation and calls each one's
/// <c>EraseAsync</c>, so each module anonymizes the residual non-PII rows it must retain (e.g. Notifications
/// blanks Title/Body; Billing keeps its append-only ledger for AML/tax).
/// </item>
/// <item>
/// It performs the crypto-shred by dispatching the internal <see cref="ShredSubjectKeyCommand"/>: the
/// subject's <see cref="Entities.SubjectKey"/> has its DEK dropped (<c>WrappedDek = null</c>) and is stamped
/// <c>DeletedAt</c>. Destroying the key renders every ciphertext encrypted under it permanently unrecoverable,
/// which satisfies erasure even for append-only stores and backups that cannot be physically rewritten. This
/// is the authoritative erasure act; the per-module anonymization above is defence-in-depth for any residue.
/// </item>
/// </list>
/// Thin PUBLIC shell (Wolverine scans <c>ExportedTypes</c>) taking only public types: it dispatches the
/// internal command via <see cref="IDispatcher"/> exactly like the other cross-module handlers, so the
/// module's Core stays internal. Boundary-clean: depends only on the Abstractions port, never on another
/// module's Core.
/// </summary>
public sealed class UserErasureRequestedHandler
{
    public async Task Handle(
        UserErasureRequested message,
        IEnumerable<IErasePersonalData> erasers,
        IDispatcher dispatcher,
        ILogger<UserErasureRequestedHandler> logger,
        CancellationToken ct)
    {
        // Per-eraser isolation (mirrors the export fan-out): one module's eraser failure must NOT block the others
        // nor — critically — the authoritative crypto-shred below. Failures are logged and counted, then the message
        // is retried so the failed module's anonymization eventually completes (every eraser + the shred is idempotent).
        var failures = 0;
        foreach (var eraser in erasers)
        {
            try
            {
                await eraser.EraseAsync(message.UserId, ct);
            }
            catch (Exception ex)
            {
                failures++;
                logger.LogError(ex,
                    "Erasure failed for module {Module}, user {UserId}; the crypto-shred still runs and the message retries.",
                    eraser.ModuleName, message.UserId);
            }
        }

        // The authoritative erasure act — destroying the subject's DEK renders every ciphertext under it
        // permanently unrecoverable. Runs even if a per-module anonymizer failed above.
        await dispatcher.Send(new ShredSubjectKeyCommand(message.UserId), ct);

        if (failures > 0)
        {
            throw new InvalidOperationException(
                $"{failures} module eraser(s) failed for subject {message.UserId}; retrying for full anonymization.");
        }
    }
}
