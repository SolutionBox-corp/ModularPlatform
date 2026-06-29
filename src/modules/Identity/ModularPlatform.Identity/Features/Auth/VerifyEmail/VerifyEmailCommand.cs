using FluentValidation;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.VerifyEmail;

public sealed record VerifyEmailCommand(string Token) : ICommand;

public sealed record VerifyEmailRequest(string Token);

internal sealed class VerifyEmailValidator : AbstractValidator<VerifyEmailCommand>
{
    public VerifyEmailValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithErrorCode("auth.email_verification_invalid");
    }
}
