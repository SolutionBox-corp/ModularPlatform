using FluentValidation;

namespace ModularPlatform.Gdpr.Features.Consents.GrantConsent;

internal sealed class GrantConsentValidator : AbstractValidator<GrantConsentCommand>
{
    public GrantConsentValidator()
    {
        RuleFor(x => x.ConsentType)
            .NotEmpty().WithErrorCode("gdpr.consent.type.required")
            .MaximumLength(128).WithErrorCode("gdpr.consent.type.too_long");

        RuleFor(x => x.PolicyVersion)
            .MaximumLength(32).WithErrorCode("gdpr.consent.policy_version.too_long");
    }
}
