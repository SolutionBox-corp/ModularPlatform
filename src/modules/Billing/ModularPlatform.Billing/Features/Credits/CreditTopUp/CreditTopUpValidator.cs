using FluentValidation;

namespace ModularPlatform.Billing.Features.Credits.CreditTopUp;

internal sealed class CreditTopUpValidator : AbstractValidator<CreditTopUpCommand>
{
    /// <summary>
    /// Sane upper bound on a single amount (1e9 credits). Caps the input so the handler's checked
    /// arithmetic (<c>account.Posted += command.Amount</c>) can never approach <see cref="long.MaxValue"/>
    /// overflow even after many top-ups; rejected before it ever reaches the ledger.
    /// </summary>
    public const long MaxAmount = 1_000_000_000L;

    public CreditTopUpValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("credit.user.required");
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode("credit.amount.must_be_positive");
        RuleFor(x => x.Amount).LessThanOrEqualTo(MaxAmount).WithErrorCode("credit.amount.too_large");
        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithErrorCode("credit.idempotency_key.required")
            .MaximumLength(256).WithErrorCode("credit.idempotency_key.too_long");
        RuleFor(x => x.BucketExpiryDays)
            .GreaterThan(0).When(x => x.BucketExpiryDays.HasValue)
            .WithErrorCode("credit.bucket_expiry.must_be_positive");
    }
}
