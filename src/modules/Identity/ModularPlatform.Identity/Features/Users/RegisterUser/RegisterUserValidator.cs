using FluentValidation;

namespace ModularPlatform.Identity.Features.Users.RegisterUser;

internal sealed class RegisterUserValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithErrorCode("user.email.required")
            .EmailAddress().WithErrorCode("user.email.invalid")
            .MaximumLength(256).WithErrorCode("user.email.too_long");

        RuleFor(x => x.Password)
            .NotEmpty().WithErrorCode("user.password.required")
            .MinimumLength(8).WithErrorCode("user.password.too_short")
            .MaximumLength(256).WithErrorCode("user.password.too_long");

        RuleFor(x => x.DisplayName)
            .MaximumLength(128).WithErrorCode("user.display_name.too_long");

        RuleFor(x => x.AcceptedTermsVersion).MaximumLength(32);
    }
}
