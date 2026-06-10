using FluentValidation;

namespace ModularPlatform.Billing.Features.Packages.UpdateCreditPackage;

internal sealed class UpdateCreditPackageValidator : AbstractValidator<UpdateCreditPackageCommand>
{
    public UpdateCreditPackageValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithErrorCode("billing.package.name.required")
            .MaximumLength(128).WithErrorCode("billing.package.name.too_long");
        RuleFor(c => c.CreditAmount)
            .GreaterThan(0).WithErrorCode("billing.package.credit_amount.must_be_positive");
        RuleFor(c => c.Price)
            .GreaterThanOrEqualTo(0).WithErrorCode("billing.package.price.must_not_be_negative");
        RuleFor(c => c.BucketExpiryDays)
            .GreaterThan(0).When(c => c.BucketExpiryDays.HasValue)
            .WithErrorCode("billing.package.bucket_expiry.must_be_positive");
        RuleFor(c => c.StripePriceId)
            .MaximumLength(256).WithErrorCode("billing.package.stripe_price.too_long");
    }
}
