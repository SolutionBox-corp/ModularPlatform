using FluentValidation;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.ResetPassword;

public sealed record ResetPasswordCommand(string Token, string NewPassword) : ICommand;

public sealed record ResetPasswordRequest(string Token, string NewPassword);

internal sealed class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithErrorCode("auth.password_reset_invalid");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithErrorCode("user.password.required")
            .MinimumLength(8).WithErrorCode("user.password.too_short")
            .MaximumLength(256).WithErrorCode("user.password.too_long");
    }
}
