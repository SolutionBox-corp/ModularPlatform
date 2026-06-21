using FluentValidation;

namespace ModularPlatform.Marketing.Features.Vibe.SendMessage;

internal sealed class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty()
            .WithErrorCode("marketing.vibe.message_required")
            .WithMessage("Message content is required.");

        RuleFor(x => x.Content)
            .MaximumLength(4000)
            .WithErrorCode("marketing.vibe.message_too_long")
            .WithMessage("Message is too long (max 4000 characters).");
    }
}
