using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ModularPlatform.Payments;

/// <summary>
/// Registers the payments building block: the per-tenant <see cref="IPaymentGatewayResolver"/> + a shared
/// <see cref="HttpClient"/> for the GoPay adapter. The module that owns the per-tenant config registers its
/// <see cref="IPaymentConfigStore"/> implementation. Call once from each module that processes payments.
/// </summary>
public static class PlatformPayments
{
    public static IServiceCollection AddPlatformPayments(this IServiceCollection services)
    {
        // One reusable HttpClient (thread-safe; absolute per-tenant URLs are built per request from the credentials).
        services.TryAddSingleton(new HttpClient());
        services.TryAddScoped<IPaymentGatewayResolver, PaymentGatewayResolver>();
        return services;
    }
}
