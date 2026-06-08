using FluentValidation;

namespace ModularPlatform.Notifications.Features.Notifications.MarkNotificationRead;

internal sealed class MarkNotificationReadValidator : AbstractValidator<MarkNotificationReadCommand>
{
    public MarkNotificationReadValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("notification.user.required");
        RuleFor(x => x.NotificationId).NotEmpty().WithErrorCode("notification.id.required");
    }
}
