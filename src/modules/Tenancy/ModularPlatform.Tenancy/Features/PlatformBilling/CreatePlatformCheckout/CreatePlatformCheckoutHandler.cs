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
            AmountMinorUnits: command.AmountMinorUnits,
            Currency: command.Currency.Trim().ToUpperInvariant(),
            Mode: CheckoutMode.Payment,
            Description: command.Description,
            Metadata: new Dictionary<string, string> { ["tenant_id"] = tenantId.ToString("N"), ["plane"] = "platform" },
            SuccessUrl: configuration["Platform:Payments:SuccessUrl"] ?? "https://app/platform/success",
            CancelUrl: configuration["Platform:Payments:CancelUrl"] ?? "https://app/platform/cancel"), ct);

        return new CreatePlatformCheckoutResponse(result.ProviderPaymentId, result.RedirectUrl);
    }
}
