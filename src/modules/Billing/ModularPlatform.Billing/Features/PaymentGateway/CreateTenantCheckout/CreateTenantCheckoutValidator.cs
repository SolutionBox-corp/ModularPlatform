using FluentValidation;

namespace ModularPlatform.Billing.Features.PaymentGateway.CreateTenantCheckout;

internal sealed class CreateTenantCheckoutValidator : AbstractValidator<CreateTenantCheckoutCommand>
{
    public CreateTenantCheckoutValidator()
    {
        RuleFor(c => c.AmountMinorUnits)
            .GreaterThan(0).WithErrorCode("billing.package.credit_amount.must_be_positive");

        RuleFor(c => c.Currency)
            .NotEmpty().WithErrorCode("billing.package.currency.required")
            .Length(3).WithErrorCode("billing.package.currency.invalid");
    }
}
