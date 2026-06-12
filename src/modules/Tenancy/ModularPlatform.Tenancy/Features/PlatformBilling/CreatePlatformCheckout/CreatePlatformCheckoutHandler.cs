using Microsoft.Extensions.Configuration;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Payments;

namespace ModularPlatform.Tenancy.Features.PlatformBilling.CreatePlatformCheckout;

internal sealed class CreatePlatformCheckoutHandler(
    IPaymentGatewayResolver resolver,
    ITenantContext tenant,
    IConfiguration configuration) : ICommandHandler<CreatePlatformCheckoutCommand, CreatePlatformCheckoutResponse>
{
    public async Task<CreatePlatformCheckoutResponse> Handle(CreatePlatformCheckoutCommand command, CancellationToken ct)
    {
        var tenantId = tenant.TenantId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        // Server-authoritative price: the caller picks a plan KEY; amount/currency come from config, never the body.
        var plan = configuration.GetSection($"Platform:Payments:Plans:{command.PlanKey.Trim()}");
        if (!plan.Exists())
        {
            throw new BusinessRuleException("tenancy.platform_plan_unknown", "Unknown platform plan.");
        }

        var amountMinorUnits = plan.GetValue<long>("AmountMinorUnits");
        if (amountMinorUnits <= 0)
        {
            throw new BusinessRuleException("tenancy.platform_plan_misconfigured", "The platform plan has no price.");
        }

        var currency = (plan["Currency"] ?? "EUR").Trim().ToUpperInvariant();
        var description = plan["Description"] ?? command.PlanKey.Trim();

        IPaymentGateway gateway;
        try
        {
            gateway = await resolver.ResolveAsync(tenantId, PaymentPlane.Platform, ct);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            throw new BusinessRuleException(ex.ErrorCode, ex.Message);
        }

        var result = await gateway.CreateCheckoutAsync(new CheckoutRequest(
            ReferenceId: Guid.CreateVersion7().ToString("N"),
            AmountMinorUnits: amountMinorUnits,
            Currency: currency,
            Mode: CheckoutMode.Payment,
            Description: description,
            Metadata: new Dictionary<string, string> { ["tenant_id"] = tenantId.ToString("N"), ["plane"] = "platform" },
            SuccessUrl: configuration["Platform:Payments:SuccessUrl"] ?? "https://app/platform/success",
            CancelUrl: configuration["Platform:Payments:CancelUrl"] ?? "https://app/platform/cancel"), ct);

        return new CreatePlatformCheckoutResponse(result.ProviderPaymentId, result.RedirectUrl);
    }
}
