using FluentValidation;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Features.Users.GetProfile;

namespace ModularPlatform.Identity.Features.Users.AcceptTerms;

public sealed record AcceptTermsCommand(Guid UserId, string TermsVersion) : ICommand<UserProfileResponse>;

public sealed record AcceptTermsRequest(string TermsVersion);

internal sealed class AcceptTermsValidator : AbstractValidator<AcceptTermsCommand>
{
    public AcceptTermsValidator()
    {
        RuleFor(x => x.TermsVersion)
            .NotEmpty().WithErrorCode("user.accepted_terms_version.required")
            .MaximumLength(32).WithErrorCode("user.accepted_terms_version.too_long");
    }
}
