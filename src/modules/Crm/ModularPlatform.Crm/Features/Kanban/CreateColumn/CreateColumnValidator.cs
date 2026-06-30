using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Kanban.CreateColumn;

internal sealed class CreateColumnValidator : AbstractValidator<CreateColumnCommand>
{
    public CreateColumnValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode("crm.column.name.required")
            .MaximumLength(128).WithErrorCode("crm.column.name.too_long");
        RuleFor(x => x.Color).MaximumLength(16).WithErrorCode("crm.column.color.too_long");
        When(x => !string.IsNullOrWhiteSpace(x.Group), () =>
            RuleFor(x => x.Group).Must(KanbanColumnGroups.IsValid).WithErrorCode("crm.column.group.invalid"));
        When(x => x.WipLimit is not null, () =>
            RuleFor(x => x.WipLimit!.Value).GreaterThan(0).LessThanOrEqualTo(999).WithErrorCode("crm.column.wip_limit.invalid"));
    }
}
