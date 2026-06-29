using FluentValidation;

namespace ModularPlatform.Crm.Features.Kanban.CreateCard;

internal sealed class CreateCardValidator : AbstractValidator<CreateCardCommand>
{
    public CreateCardValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode("crm.card.title.required")
            .MaximumLength(256).WithErrorCode("crm.card.title.too_long");
        RuleFor(x => x.Description).MaximumLength(8192).WithErrorCode("crm.card.description.too_long");
    }
}
