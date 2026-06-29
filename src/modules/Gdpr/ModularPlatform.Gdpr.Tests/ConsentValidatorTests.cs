using FluentValidation.Results;
using ModularPlatform.Gdpr.Features.Consents.GrantConsent;
using ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;
using Shouldly;

namespace ModularPlatform.Gdpr.Tests;

public sealed class ConsentValidatorTests
{
    [Fact]
    public void Grant_consent_validator_uses_stable_error_codes()
    {
        var result = new GrantConsentValidator().Validate(new GrantConsentCommand(
            UserId: Guid.Empty,
            ConsentType: "",
            PolicyVersion: new string('v', 33)));

        ErrorCodes(result).ShouldBe([
            "gdpr.consent.user_id.required",
            "gdpr.consent.type.required",
            "gdpr.consent.policy_version.too_long"
        ]);
    }

    [Fact]
    public void Withdraw_consent_validator_uses_stable_error_codes()
    {
        var result = new WithdrawConsentValidator().Validate(new WithdrawConsentCommand(
            UserId: Guid.Empty,
            ConsentType: new string('c', 129),
            PolicyVersion: new string('v', 33)));

        ErrorCodes(result).ShouldBe([
            "gdpr.consent.user_id.required",
            "gdpr.consent.type.too_long",
            "gdpr.consent.policy_version.too_long"
        ]);
    }

    [Fact]
    public void Consent_validators_accept_valid_input()
    {
        var userId = Guid.CreateVersion7();

        new GrantConsentValidator().Validate(
            new GrantConsentCommand(userId, "marketing", "2026-06")).IsValid.ShouldBeTrue();
        new WithdrawConsentValidator().Validate(
            new WithdrawConsentCommand(userId, "marketing", "2026-06")).IsValid.ShouldBeTrue();
    }

    private static string[] ErrorCodes(ValidationResult result) =>
        result.Errors.Select(e => e.ErrorCode).ToArray();
}
