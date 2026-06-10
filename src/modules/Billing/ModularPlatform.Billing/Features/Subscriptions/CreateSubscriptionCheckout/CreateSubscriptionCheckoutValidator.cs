using FluentValidation;

namespace ModularPlatform.Billing.Features.Subscriptions.CreateSubscriptionCheckout;

internal sealed class CreateSubscriptionCheckoutValidator : AbstractValidator<CreateSubscriptionCheckoutCommand>
{
    public CreateSubscriptionCheckoutValidator()
    {
        RuleFor(c => c.PlanKey)
            .NotEmpty().WithErrorCode("billing.subscription.plan_key.required")
            .MaximumLength(64).WithErrorCode("billing.subscription.plan_key.too_long");
    }
}
