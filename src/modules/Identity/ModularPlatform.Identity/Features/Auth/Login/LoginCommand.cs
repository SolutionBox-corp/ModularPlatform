using FluentValidation;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password) : ICommand<AuthTokensResponse>;

public sealed record LoginRequest(string Email, string Password);

internal sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().WithErrorCode("user.email.required");
        RuleFor(x => x.Password).NotEmpty().WithErrorCode("user.password.required");
    }
}
