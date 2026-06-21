using FluentValidation;

namespace ModularPlatform.Marketing.Features.Vibe.StreamMessage;

/// <summary>Same content rules as the 202 <c>SendMessage</c> path (shared error codes).</summary>
internal sealed class BeginStreamMessageValidator : AbstractValidator<BeginStreamMessageCommand>
{
    public BeginStreamMessageValidator()
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
