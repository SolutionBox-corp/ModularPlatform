using FluentValidation;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : ICommand<ForgotPasswordResponse>;

public sealed record ForgotPasswordRequest(string Email);

public sealed record ForgotPasswordResponse(bool Accepted);

internal sealed class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithErrorCode("user.email.required")
            .EmailAddress().WithErrorCode("user.email.invalid")
            .MaximumLength(320).WithErrorCode("user.email.too_long");
    }
}
