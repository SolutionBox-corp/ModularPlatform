using FluentValidation;

namespace ModularPlatform.Operations.Features.DemoInvoke;

internal sealed class InvokeDemoCheckValidator : AbstractValidator<InvokeDemoCheckCommand>
{
    public InvokeDemoCheckValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithErrorCode("auth.required");
        RuleFor(x => x.Input).InclusiveBetween(0, 50).WithErrorCode("operations.demo_input_invalid");
        RuleFor(x => x.TimeoutMs).InclusiveBetween(50, 3_000).WithErrorCode("operations.demo_timeout_invalid");
        RuleFor(x => x.WorkDelayMs).InclusiveBetween(0, 5_000).WithErrorCode("operations.demo_delay_invalid");
    }
}
