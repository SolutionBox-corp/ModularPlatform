using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Entities;
using ModularPlatform.Notifications.Persistence;

namespace ModularPlatform.Notifications.Features.Preferences.SetNotificationPreference;

internal sealed class SetNotificationPreferenceHandler(NotificationsDbContext db)
    : ICommandHandler<SetNotificationPreferenceCommand, SetNotificationPreferenceResponse>
{
    public async Task<SetNotificationPreferenceResponse> Handle(SetNotificationPreferenceCommand command, CancellationToken ct)
    {
        var channel = command.Channel.Trim().ToLowerInvariant();
        var preference = await db.NotificationPreferences
            .FirstOrDefaultAsync(p => p.UserId == command.UserId && p.Channel == channel, ct);

        if (preference is null)
        {
            preference = new NotificationPreference
            {
                UserId = command.UserId,
                Channel = channel,
                Enabled = command.Enabled,
            };
            db.NotificationPreferences.Add(preference);
        }
        else
        {
            preference.Enabled = command.Enabled;
        }

        await db.SaveChangesAsync(ct);
        return new SetNotificationPreferenceResponse(channel, command.Enabled);
    }
}
