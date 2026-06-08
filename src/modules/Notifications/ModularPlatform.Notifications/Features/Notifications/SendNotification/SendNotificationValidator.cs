using FluentValidation;

namespace ModularPlatform.Notifications.Features.Notifications.SendNotification;

internal sealed class SendNotificationValidator : AbstractValidator<SendNotificationCommand>
{
    private static readonly string[] AllowedChannels = ["email", "push", "inapp"];

    public SendNotificationValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithErrorCode("notification.user.required");

        RuleFor(x => x.TemplateKey)
            .NotEmpty().WithErrorCode("notification.template_key.required")
            .MaximumLength(128).WithErrorCode("notification.template_key.too_long");

        RuleFor(x => x.Channels)
            .NotEmpty().WithErrorCode("notification.channels.required");

        RuleForEach(x => x.Channels)
            .Must(c => AllowedChannels.Contains(c)).WithErrorCode("notification.channel.invalid");
    }
}
