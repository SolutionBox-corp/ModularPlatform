using FluentValidation;
using ModularPlatform.Crm.Entities;

namespace ModularPlatform.Crm.Features.Deals.CreateDeal;

internal sealed class CreateDealValidator : AbstractValidator<CreateDealCommand>
{
    public CreateDealValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode("crm.deal.title.required")
            .MaximumLength(256).WithErrorCode("crm.deal.title.too_long");

        RuleFor(x => x.AmountCents).GreaterThanOrEqualTo(0).WithErrorCode("crm.deal.amount.invalid");

        RuleFor(x => x.Currency)
            .Length(3).WithErrorCode("crm.deal.currency.invalid");

        RuleFor(x => x.Stage)
            .Must(DealStages.IsValid).WithErrorCode("crm.deal.stage.invalid");

        RuleFor(x => x.Notes).MaximumLength(8192).WithErrorCode("crm.deal.notes.too_long");
    }
}
