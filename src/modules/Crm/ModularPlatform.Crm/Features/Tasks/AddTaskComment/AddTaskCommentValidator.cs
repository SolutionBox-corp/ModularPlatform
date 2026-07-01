using FluentValidation;

namespace ModularPlatform.Crm.Features.Tasks.AddTaskComment;

internal sealed class AddTaskCommentValidator : AbstractValidator<AddTaskCommentCommand>
{
    public AddTaskCommentValidator()
    {
        RuleFor(x => x.Body)
            .NotEmpty().WithErrorCode("crm.task.comment.body.required")
            .MaximumLength(8192).WithErrorCode("crm.task.comment.body.too_long");
    }
}
