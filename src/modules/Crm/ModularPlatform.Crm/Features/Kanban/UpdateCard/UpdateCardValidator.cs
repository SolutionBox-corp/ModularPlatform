using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Kanban.UpdateCard;

internal sealed class UpdateCardValidator : AbstractValidator<UpdateCardCommand>
{
    public UpdateCardValidator()
    {
        When(x => x.Title is not null, () =>
            RuleFor(x => x.Title)
                .NotEmpty().WithErrorCode("crm.card.title.required")
                .MaximumLength(256).WithErrorCode("crm.card.title.too_long"));

        RuleFor(x => x.Description).MaximumLength(8192).WithErrorCode("crm.card.description.too_long");

        When(x => !string.IsNullOrWhiteSpace(x.Priority), () =>
            RuleFor(x => x.Priority).Must(TaskPriorities.IsValid).WithErrorCode("crm.card.priority.invalid"));

        When(x => x.Labels is not null, () =>
        {
            RuleFor(x => x.Labels!).Must(labels => labels.Length <= 16).WithErrorCode("crm.card.labels.too_many");
            RuleForEach(x => x.Labels!).MaximumLength(32).WithErrorCode("crm.card.label.too_long");
        });
    }
}
