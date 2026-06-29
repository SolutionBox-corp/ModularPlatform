using FluentValidation;
using ModularPlatform.Billing.Contracts;

namespace ModularPlatform.Billing.Features.Credits.ReleaseHold;

internal sealed class ReleaseHoldValidator : AbstractValidator<ReleaseHoldCommand>
{
    public ReleaseHoldValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("credit.user.required");
        RuleFor(x => x.ReservationId).NotEmpty().WithErrorCode("credit.reservation.required");
    }
}
