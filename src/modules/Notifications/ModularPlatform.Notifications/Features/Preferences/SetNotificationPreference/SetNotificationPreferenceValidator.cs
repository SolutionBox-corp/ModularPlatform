using FluentValidation;
using ModularPlatform.Notifications.Features.Preferences;

namespace ModularPlatform.Notifications.Features.Preferences.SetNotificationPreference;

internal sealed class SetNotificationPreferenceValidator : AbstractValidator<SetNotificationPreferenceCommand>
{
    public SetNotificationPreferenceValidator()
    {
        RuleFor(c => c.UserId)
            .NotEmpty().WithErrorCode("notification.user.required");

        RuleFor(c => c.Channel)
            .NotEmpty().WithErrorCode("notification.channel.required")
            .Must(c => NotificationChannels.IsConfigurable(c.Trim().ToLowerInvariant()))
            .WithErrorCode("notification.channel_preference.invalid");
    }
}
