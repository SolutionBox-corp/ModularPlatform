using ModularPlatform.Abstractions;
using ModularPlatform.Gdpr.Contracts;

namespace ModularPlatform.Gdpr.Messaging;

/// <summary>
/// In-proc erasure path. Wolverine auto-discovers this handler and the Worker runs it when a
/// <see cref="UserErasureRequested"/> event is published; the inbox dedups it. It fans out across every
/// module's <see cref="IErasePersonalData"/> implementation and calls each one's <c>EraseAsync</c>.
/// Boundary-clean: depends only on the Abstractions port, never on another module's Core. Modules may
/// also subscribe to the event directly with their own handler — both paths converge on EraseAsync.
/// </summary>
public sealed class UserErasureRequestedHandler
{
    public async Task Handle(
        UserErasureRequested message,
        IEnumerable<IErasePersonalData> erasers,
        CancellationToken ct)
    {
        foreach (var eraser in erasers)
        {
            await eraser.EraseAsync(message.UserId, ct);
        }
    }
}
