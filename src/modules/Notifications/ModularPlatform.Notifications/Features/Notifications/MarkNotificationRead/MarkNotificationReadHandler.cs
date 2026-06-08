using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Persistence;

namespace ModularPlatform.Notifications.Features.Notifications.MarkNotificationRead;

/// <summary>
/// Write WITHOUT an integration event: injects the scoped DbContext directly and SaveChangesAsync.
/// Stamps ReadAt only if the notification belongs to the caller and is still unread (idempotent).
/// </summary>
internal sealed class MarkNotificationReadHandler(NotificationsDbContext db, IClock clock)
    : ICommandHandler<MarkNotificationReadCommand, Unit>
{
    public async Task<Unit> Handle(MarkNotificationReadCommand command, CancellationToken ct)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == command.NotificationId && n.UserId == command.UserId, ct)
            ?? throw new NotFoundException("notification.not_found", "Notification not found.");

        if (notification.ReadAt is null)
        {
            notification.ReadAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return Unit.Value;
    }
}
