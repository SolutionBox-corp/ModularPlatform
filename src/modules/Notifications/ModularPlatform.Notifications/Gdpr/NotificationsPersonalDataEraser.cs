using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Notifications.Persistence;

namespace ModularPlatform.Notifications.Gdpr;

/// <summary>
/// GDPR erasure port for Notifications. The in-app feed rows carry rendered PII in <c>Title</c> and
/// <c>Body</c> (e.g. the subject's name/email substituted from a template). On erasure those free-text
/// columns are anonymized in place — the rows themselves are kept so the feed's structural integrity
/// (counts, read state, timestamps) survives, but no personal content remains.
/// <para>
/// Implemented with EF / LINQ only (no raw SQL). Uses an atomic <c>ExecuteUpdate</c> set-based write: it
/// scrubs every matching row in a single round-trip without loading them, and because both columns are
/// NOT NULL they are blanked to <see cref="string.Empty"/> rather than null. The operation is idempotent —
/// re-running it on already-scrubbed rows is a harmless no-op. Runs in the Worker's system context
/// (no tenant), so the tenant query filter does not restrict the match.
/// </para>
/// </summary>
internal sealed class NotificationsPersonalDataEraser(NotificationsDbContext db) : IErasePersonalData
{
    public string ModuleName => "Notifications";

    public async Task EraseAsync(Guid userId, CancellationToken ct)
    {
        await db.Notifications
            .Where(n => n.UserId == userId)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(n => n.Title, string.Empty)
                    .SetProperty(n => n.Body, string.Empty),
                ct);

        await db.NotificationPreferences
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync(ct);
    }
}
