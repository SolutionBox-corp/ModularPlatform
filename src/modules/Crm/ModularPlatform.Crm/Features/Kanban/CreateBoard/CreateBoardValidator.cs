using FluentValidation;

namespace ModularPlatform.Crm.Features.Kanban.CreateBoard;

internal sealed class CreateBoardValidator : AbstractValidator<CreateBoardCommand>
{
    public CreateBoardValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithErrorCode("crm.board.name.required")
            .MaximumLength(256).WithErrorCode("crm.board.name.too_long");
    }
}
