using FluentValidation;

namespace ModularPlatform.Crm.Features.Kanban.CreateColumn;

internal sealed class CreateColumnValidator : AbstractValidator<CreateColumnCommand>
{
    public CreateColumnValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode("crm.column.name.required")
            .MaximumLength(128).WithErrorCode("crm.column.name.too_long");
    }
}
