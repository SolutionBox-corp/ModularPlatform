using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Features.Credits.ConfirmSpend;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Billing.Features.Credits.GetCreditBalance;
using ModularPlatform.Billing.Features.Credits.ReleaseHold;
using ModularPlatform.Billing.Features.Credits.ReserveCredits;
using ModularPlatform.Billing.Features.Stripe.StripeWebhook;
using ModularPlatform.Billing.Gdpr;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Security;
using ModularPlatform.Cqrs;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

namespace ModularPlatform.Billing;

/// <summary>
/// Billing module wiring (mirrors <c>IdentityModule</c>). Owns the append-only double-entry credit ledger,
/// Stripe webhook ingest, and the credit-account provisioning handler. Gated on <c>Modules:Billing:Enabled</c>.
/// Consumes <c>UserRegisteredIntegrationEvent</c> (auto-discovered by Wolverine) and publishes
/// <c>CreditsToppedUp</c> / <c>CreditsSpent</c> via the outbox. No code outside this assembly references its Core.
/// </summary>
public sealed class BillingModule : IModule
{
    public string Name => "Billing";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(BillingModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(BillingModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<BillingDbContext>(Name, write);
        services.AddModuleReadDbContext<BillingDbContext>(read);

        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));

        services.AddScoped<IExportPersonalData, BillingPersonalDataExporter>();
        services.AddScoped<IErasePersonalData, BillingPersonalDataEraser>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGetCreditBalance();
        endpoints.MapCreditTopUp();
        endpoints.MapReserveCredits();
        endpoints.MapConfirmSpend();
        endpoints.MapReleaseHold();
        endpoints.MapStripeWebhook();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Register this module's message handlers EXPLICITLY (conventional cross-assembly discovery is not
        // reliable for module assemblies). Billing publishes CreditsToppedUp / CreditsSpent via the outbox.
        options.Discovery.IncludeType<Messaging.ProvisionCreditAccountHandler>();
        options.Discovery.IncludeType<Messaging.ProcessStripeEventHandler>();
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        // Migrations run on the ADMIN connection — the DI-registered context uses the RLS runtime role,
        // which cannot run DDL. The RLS bootstrapper (host, post-migration) then provisions role + policies.
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<BillingDbContext>(services, adminConnectionString, Name, ct);
    }
}
