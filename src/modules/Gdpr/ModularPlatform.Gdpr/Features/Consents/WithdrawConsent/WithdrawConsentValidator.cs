using FluentValidation;

namespace ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;

internal sealed class WithdrawConsentValidator : AbstractValidator<WithdrawConsentCommand>
{
    public WithdrawConsentValidator()
    {
        RuleFor(x => x.ConsentType)
            .NotEmpty().WithErrorCode("gdpr.consent.type.required")
            .MaximumLength(128).WithErrorCode("gdpr.consent.type.too_long");

        RuleFor(x => x.PolicyVersion)
            .MaximumLength(32).WithErrorCode("gdpr.consent.policy_version.too_long");
    }
}
