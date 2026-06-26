using Microsoft.Extensions.Configuration;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Payments;

namespace ModularPlatform.Billing.Features.PaymentGateway.CreateTenantCheckout;

internal sealed class CreateTenantCheckoutHandler(
    IPaymentGatewayResolver resolver,
    ITenantContext tenant,
    IConfiguration configuration) : ICommandHandler<CreateTenantCheckoutCommand, CreateTenantCheckoutResponse>
{
    public async Task<CreateTenantCheckoutResponse> Handle(CreateTenantCheckoutCommand command, CancellationToken ct)
    {
        var tenantId = tenant.TenantId
            ?? throw new UnauthorizedException("auth.required", "Authentication required.");

        IPaymentGateway gateway;
        try
        {
            gateway = await resolver.ResolveAsync(tenantId, PaymentPlane.Tenant, ct);
        }
        catch (PaymentGatewayUnavailableException ex)
        {
            // Map the building-block failure into the platform's domain-error pipeline (RFC 9457 4xx).
            throw new BusinessRuleException(ex.ErrorCode, ex.Message);
        }

        CheckoutResult result;
        try
        {
            result = await gateway.CreateCheckoutAsync(new CheckoutRequest(
                ReferenceId: Guid.CreateVersion7().ToString("N"),
                AmountMinorUnits: command.AmountMinorUnits,
                Currency: command.Currency.Trim().ToUpperInvariant(),
                Mode: CheckoutMode.Payment,
                Description: command.Description,
                Metadata: new Dictionary<string, string> { ["tenant_id"] = tenantId.ToString("N") },
                SuccessUrl: configuration["Billing:Payments:SuccessUrl"] ?? "https://app/checkout/success",
                CancelUrl: configuration["Billing:Payments:CancelUrl"] ?? "https://app/checkout/cancel"), ct);
        }
        catch (Exception ex) when (ex is not ModularPlatformException and not OperationCanceledException)
        {
            throw new BusinessRuleException("payment.gateway_inactive", "The payment gateway for this workspace is unavailable.");
        }

        return new CreateTenantCheckoutResponse(result.ProviderPaymentId, result.RedirectUrl);
    }
}
