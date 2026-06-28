using FluentValidation.Results;
using ModularPlatform.Identity.Features.Users.RegisterUser;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

public sealed class RegisterUserValidatorTests
{
    [Fact]
    public void Registration_validator_uses_stable_dotted_error_codes()
    {
        var command = new RegisterUserCommand(
            Email: "",
            Password: "",
            DisplayName: new string('x', 129),
            AcceptedTermsVersion: new string('v', 33));

        var result = new RegisterUserValidator().Validate(command);

        ErrorCodes(result).ShouldBe([
            "user.email.required",
            "user.email.invalid",
            "user.password.required",
            "user.password.too_short",
            "user.display_name.too_long",
            "user.accepted_terms_version.too_long"
        ]);
    }

    [Fact]
    public void Registration_validator_accepts_valid_input()
    {
        var command = new RegisterUserCommand(
            Email: "person@example.com",
            Password: "S3cure!pass",
            DisplayName: "Person",
            AcceptedTermsVersion: "terms-2026-06");

        new RegisterUserValidator().Validate(command).IsValid.ShouldBeTrue();
    }

    private static string[] ErrorCodes(ValidationResult result) =>
        result.Errors.Select(e => e.ErrorCode).ToArray();
}
