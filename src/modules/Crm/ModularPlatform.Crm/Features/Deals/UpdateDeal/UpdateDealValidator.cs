using FluentValidation;

namespace ModularPlatform.Crm.Features.Deals.UpdateDeal;

internal sealed class UpdateDealValidator : AbstractValidator<UpdateDealCommand>
{
    public UpdateDealValidator()
    {
        When(x => x.Title is not null, () =>
            RuleFor(x => x.Title)
                .NotEmpty().WithErrorCode("crm.deal.title.required")
                .MaximumLength(256).WithErrorCode("crm.deal.title.too_long"));

        When(x => x.AmountCents is not null, () =>
            RuleFor(x => x.AmountCents!.Value).GreaterThanOrEqualTo(0).WithErrorCode("crm.deal.amount.invalid"));

        When(x => x.Currency is not null, () =>
            RuleFor(x => x.Currency).Length(3).WithErrorCode("crm.deal.currency.invalid"));

        RuleFor(x => x.Notes).MaximumLength(8192).WithErrorCode("crm.deal.notes.too_long");
    }
}
