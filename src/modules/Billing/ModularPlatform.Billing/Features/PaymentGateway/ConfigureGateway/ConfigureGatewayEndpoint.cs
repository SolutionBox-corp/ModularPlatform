using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Billing.Features.PaymentGateway.ConfigureGateway;

internal static class ConfigureGatewayEndpoint
{
    public static void MapConfigureGateway(this IEndpointRouteBuilder app)
    {
        app.MapPut("/billing/payment-gateway", async (
                ConfigureGatewayRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var result = await dispatcher.Send(new ConfigureGatewayCommand(
                    request.Provider, request.Currency, request.StripeApiKey, request.StripeWebhookSecret,
                    request.GoPayGoid, request.GoPayClientId, request.GoPayClientSecret, request.Sandbox), ct);
                return Results.Ok(ApiResponse<ConfigureGatewayResponse>.Ok(result));
            })
            .RequirePermission(PlatformPermissions.BillingManage)
            .RequireModule("billing")
            .WithTags("Billing")
            .WithName("ConfigurePaymentGateway");
    }
}
