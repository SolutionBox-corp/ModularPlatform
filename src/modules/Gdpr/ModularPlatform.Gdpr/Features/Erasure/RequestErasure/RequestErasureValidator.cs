using FluentValidation;

namespace ModularPlatform.Gdpr.Features.Erasure.RequestErasure;

internal sealed class RequestErasureValidator : AbstractValidator<RequestErasureCommand>
{
    public RequestErasureValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithErrorCode("gdpr.erasure.user_id.required");
    }
}
