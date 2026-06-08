using FluentValidation;

namespace ModularPlatform.Billing.Features.Credits.ConfirmSpend;

internal sealed class ConfirmSpendValidator : AbstractValidator<ConfirmSpendCommand>
{
    public ConfirmSpendValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("credit.user.required");
        RuleFor(x => x.ReservationId).NotEmpty().WithErrorCode("credit.reservation.required");
    }
}
