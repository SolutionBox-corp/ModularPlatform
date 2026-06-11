using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace ModularPlatform.Billing.Security;

/// <summary>
/// Fails the host at startup (<c>ValidateOnStart</c>) if the in-memory <c>FakeStripeGateway</c> is enabled in
/// Production. The fake accepts any (empty) webhook signature and mints credits without ever touching Stripe — it
/// is a TEST-ONLY seam. Shipping it to Production would let anyone forge a paid purchase. Non-production
/// environments (Development, Testing, local) are exempt so the harness and local runs work.
/// </summary>
internal sealed class StripeOptionsValidator(IHostEnvironment environment) : IValidateOptions<StripeOptions>
{
    public ValidateOptionsResult Validate(string? name, StripeOptions options)
    {
        if (options.UseFakeGateway && environment.IsProduction())
        {
            return ValidateOptionsResult.Fail(
                "Billing:Stripe:UseFakeGateway must be false in Production — the in-memory fake gateway is a "
                + "test-only seam that bypasses real payment verification.");
        }

        return ValidateOptionsResult.Success;
    }
}
