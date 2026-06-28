using FluentValidation;
using ModularPlatform.Billing.Contracts;

namespace ModularPlatform.Billing.Features.Credits.ReserveCredits;

internal sealed class ReserveCreditsValidator : AbstractValidator<ReserveCreditsCommand>
{
    /// <summary>
    /// Sane upper bound on a single reservation (1e9 credits). Caps the input so the handler's
    /// <c>Pending += amount</c> / <c>Available -= amount</c> arithmetic can never overflow
    /// <see cref="long.MaxValue"/>; rejected before it reaches the atomic ExecuteUpdate guard.
    /// </summary>
    public const long MaxAmount = 1_000_000_000L;

    public ReserveCreditsValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("credit.user.required");
        RuleFor(x => x.Amount).GreaterThan(0).WithErrorCode("credit.amount.must_be_positive");
        RuleFor(x => x.Amount).LessThanOrEqualTo(MaxAmount).WithErrorCode("credit.amount.too_large");
        RuleFor(x => x.HoldMinutes)
            .GreaterThan(0).When(x => x.HoldMinutes.HasValue)
            .WithErrorCode("credit.hold_minutes.must_be_positive");
    }
}
