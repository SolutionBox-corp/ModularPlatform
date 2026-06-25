using FluentValidation;

namespace ModularPlatform.Identity.Features.Users.ChangePassword;

/// <summary>
/// Validates a password change. The NEW password follows the same strength rules as registration (reused error
/// codes). The "new must differ from current" check needs the stored hash, so it lives in the handler, not here.
/// </summary>
internal sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithErrorCode("user.current_password.required");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithErrorCode("user.password.required")
            .MinimumLength(8).WithErrorCode("user.password.too_short")
            .MaximumLength(256).WithErrorCode("user.password.too_long");
    }
}
