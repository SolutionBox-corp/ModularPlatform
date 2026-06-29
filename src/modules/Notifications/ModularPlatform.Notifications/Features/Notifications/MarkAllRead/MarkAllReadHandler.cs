using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Persistence;

namespace ModularPlatform.Notifications.Features.Notifications.MarkAllRead;

/// <summary>
/// Bulk write WITHOUT an integration event: stamps ReadAt on ALL of the caller's still-unread notifications
/// in one atomic ExecuteUpdateAsync (WHERE UserId == me &amp;&amp; ReadAt == null). Idempotent — a second call
/// affects 0 rows. NOTE: ExecuteUpdate bypasses the audit interceptor + xmin, which is fine for a read-flag flip.
/// Mirrors MarkNotificationRead for the read-flag column name and ownership.
/// </summary>
internal sealed class MarkAllReadHandler(NotificationsDbContext db, IClock clock)
    : ICommandHandler<MarkAllReadCommand, MarkAllReadResponse>
{
    public async Task<MarkAllReadResponse> Handle(MarkAllReadCommand command, CancellationToken ct)
    {
        var now = clock.UtcNow;

        var marked = await db.Notifications
            .Where(n => n.UserId == command.UserId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now), ct);

        return new MarkAllReadResponse(marked);
    }
}
