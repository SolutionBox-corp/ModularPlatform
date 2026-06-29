using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Tasks.CreateTask;

internal sealed class CreateTaskValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode("crm.task.title.required")
            .MaximumLength(256).WithErrorCode("crm.task.title.too_long");

        RuleFor(x => x.Description).MaximumLength(8192).WithErrorCode("crm.task.description.too_long");

        RuleFor(x => x.Priority).Must(TaskPriorities.IsValid).WithErrorCode("crm.task.priority.invalid");
    }
}
