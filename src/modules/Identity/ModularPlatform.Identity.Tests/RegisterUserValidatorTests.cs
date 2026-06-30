using FluentValidation.Results;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.Identity.Features.Users.RegisterUser;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

[Collection("Integration")]
public sealed class RegisterUserValidatorTests(PlatformApiFactory fixture)
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

    [Fact]
    public async Task Registration_validation_errors_are_returned_as_problem_details_field_error_codes()
    {
        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new
        {
            email = "",
            password = "short",
            displayName = new string('x', 129),
            acceptedTermsVersion = new string('v', 33),
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("errorCode").GetString().ShouldBe("validation.failed");
        var errors = body.GetProperty("errors").EnumerateArray()
            .Select(e => new
            {
                Field = e.GetProperty("field").GetString(),
                ErrorCode = e.GetProperty("errorCode").GetString(),
            })
            .ToArray();

        errors.ShouldContain(e => e.Field == "Email" && e.ErrorCode == "user.email.required");
        errors.ShouldContain(e => e.Field == "Email" && e.ErrorCode == "user.email.invalid");
        errors.ShouldContain(e => e.Field == "Password" && e.ErrorCode == "user.password.too_short");
        errors.ShouldContain(e => e.Field == "DisplayName" && e.ErrorCode == "user.display_name.too_long");
        errors.ShouldContain(e => e.Field == "AcceptedTermsVersion"
                                  && e.ErrorCode == "user.accepted_terms_version.too_long");
    }

    private static string[] ErrorCodes(ValidationResult result) =>
        result.Errors.Select(e => e.ErrorCode).ToArray();
}
