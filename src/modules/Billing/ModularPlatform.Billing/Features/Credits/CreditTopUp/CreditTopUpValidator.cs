using FluentValidation;

namespace ModularPlatform.Billing.Features.Credits.CreditTopUp;

internal sealed class CreditTopUpValidator : AbstractValidator<CreditTopUpCommand>
{
    public CreditTopUpValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("credit.user.required");
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode("credit.amount.must_be_positive");
        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithErrorCode("credit.idempotency_key.required")
            .MaximumLength(256).WithErrorCode("credit.idempotency_key.too_long");
        RuleFor(x => x.BucketExpiryDays)
            .GreaterThan(0).When(x => x.BucketExpiryDays.HasValue)
            .WithErrorCode("credit.bucket_expiry.must_be_positive");
    }
}
