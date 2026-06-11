using FluentValidation;

namespace ModularPlatform.Identity.Features.Admin.IssueMachineToken;

internal sealed class IssueMachineTokenValidator : AbstractValidator<IssueMachineTokenCommand>
{
    public IssueMachineTokenValidator()
    {
        RuleFor(c => c.TenantId).NotEmpty().WithErrorCode("machine_token.tenant_required");
        RuleFor(c => c.Name).NotEmpty().WithErrorCode("machine_token.name_required")
            .MaximumLength(128).WithErrorCode("machine_token.name_too_long");
    }
}
