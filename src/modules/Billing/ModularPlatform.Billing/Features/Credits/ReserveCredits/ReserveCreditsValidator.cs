using FluentValidation;

namespace ModularPlatform.Billing.Features.Credits.ReserveCredits;

internal sealed class ReserveCreditsValidator : AbstractValidator<ReserveCreditsCommand>
{
    public ReserveCreditsValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("credit.user.required");
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode("credit.amount.must_be_positive");
        RuleFor(x => x.HoldMinutes)
            .GreaterThan(0).When(x => x.HoldMinutes.HasValue)
            .WithErrorCode("credit.hold_minutes.must_be_positive");
    }
}
