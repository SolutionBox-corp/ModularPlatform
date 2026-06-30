using FluentValidation;

namespace ModularPlatform.Operations.Features.Demo;

internal sealed class StartDemoOperationValidator : AbstractValidator<StartDemoOperationCommand>
{
    public StartDemoOperationValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("auth.required");
        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(256)
            .WithErrorCode("operations.idempotency_key.too_long")
            .When(x => x.IdempotencyKey is not null);
    }
}
