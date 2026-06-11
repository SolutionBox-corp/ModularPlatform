using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using ModularPlatform.Secrets;
using ModularPlatform.Payments;
using ModularPlatform.Tenancy.Features.Admin.GetPlatformBillingStatus;
using ModularPlatform.Tenancy.Features.Admin.ProvisionTenant;
using ModularPlatform.Tenancy.Features.Admin.SetEntitlement;
using ModularPlatform.Tenancy.Features.Entitlements.GetMyEntitlements;
using ModularPlatform.Tenancy.Persistence;
using ModularPlatform.Tenancy.Services;
using FluentValidation;
using Wolverine;

namespace ModularPlatform.Tenancy;

/// <summary>
/// Tenancy module — owns the platform tenant registry (<c>tenants</c>) + per-tenant module entitlements
/// (<c>tenant_entitlements</c>), and the control-plane ports (<see cref="ITenantDirectory"/>,
/// <see cref="IEntitlementResolver"/>, <see cref="ITenantProvisioning"/>) that other modules and the request edge
/// consume. Gated on <c>Modules:Tenancy:Enabled</c>. Platform-admin commands + the entitlement guard layer on next.
/// </summary>
public sealed class TenancyModule : IModule
{
    public string Name => "Tenancy";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(TenancyModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(TenancyModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<TenancyDbContext>(Name, write);
        services.AddModuleReadDbContext<TenancyDbContext>(read);

        // Control-plane ports (consumed cross-module via Abstractions; impls stay internal).
        services.AddScoped<ITenantDirectory, TenantDirectory>();
        services.AddScoped<IEntitlementResolver, EntitlementResolver>();
        services.AddScoped<ITenantProvisioning, TenantProvisioning>();

        // Platform-plane payments (tenant pays the SaaS): reuse the shared Payments building-block; this store
        // serves PaymentPlane.Platform and coexists with Billing's tenant-plane store (resolver picks by plane).
        services.AddPlatformSecrets(configuration);
        services.AddPlatformPayments();
        services.AddScoped<ModularPlatform.Payments.IPaymentConfigStore, Services.TenancyPlatformPaymentConfigStore>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGetMyEntitlements();
        endpoints.MapProvisionTenant();
        endpoints.MapSetEntitlement();
        endpoints.MapGetPlatformBillingStatus();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Tenancy publishes TenantProvisionedIntegrationEvent (via the outbox); it consumes nothing yet.
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<TenancyDbContext>(services, adminConnectionString, Name, ct);
    }
}
