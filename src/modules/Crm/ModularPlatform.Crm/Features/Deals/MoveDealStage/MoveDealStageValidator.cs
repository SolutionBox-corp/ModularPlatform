using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Deals.MoveDealStage;

internal sealed class MoveDealStageValidator : AbstractValidator<MoveDealStageCommand>
{
    public MoveDealStageValidator()
    {
        RuleFor(x => x.Stage).Must(DealStages.IsValid).WithErrorCode("crm.deal.stage.invalid");
    }
}
