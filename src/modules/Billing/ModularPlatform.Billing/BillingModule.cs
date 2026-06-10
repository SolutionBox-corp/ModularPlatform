using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Features.Coupons.ValidatePromoCode;
using ModularPlatform.Billing.Features.Credits.ConfirmSpend;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Billing.Features.Credits.GetCreditBalance;
using ModularPlatform.Billing.Features.Credits.ReleaseHold;
using ModularPlatform.Billing.Features.Credits.ReserveCredits;
using ModularPlatform.Billing.Features.Packages.CreateCreditPackage;
using ModularPlatform.Billing.Features.Packages.ListCreditPackages;
using ModularPlatform.Billing.Features.Packages.PurchaseCreditPackage;
using ModularPlatform.Billing.Features.Packages.UpdateCreditPackage;
using ModularPlatform.Billing.Features.Purchases.GetCreditPurchase;
using ModularPlatform.Billing.Features.Stripe.StripeWebhook;
using ModularPlatform.Billing.Features.Subscriptions.CancelSubscription;
using ModularPlatform.Billing.Features.Subscriptions.CreateSubscriptionCheckout;
using ModularPlatform.Billing.Features.Subscriptions.GetMySubscription;
using ModularPlatform.Billing.Features.Subscriptions.GetSubscriptionPlans;
using ModularPlatform.Billing.Gdpr;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Sagas;
using ModularPlatform.Billing.Security;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Quartz;
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
        services.Configure<SubscriptionOptions>(configuration.GetSection(SubscriptionOptions.SectionName));

        // The ONE Stripe seam. The in-memory fake (test harness only) makes the full worker path —
        // ledger top-up, ProcessedAt, saga transitions — assertable without the network.
        if (configuration.GetValue<bool>($"{StripeOptions.SectionName}:UseFakeGateway"))
        {
            services.AddSingleton<FakeStripeGateway>();
            services.AddSingleton<IStripeGateway>(sp => sp.GetRequiredService<FakeStripeGateway>());
        }
        else
        {
            services.AddSingleton<IStripeGateway, StripeGateway>();
        }

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
        endpoints.MapListCreditPackages();
        endpoints.MapPurchaseCreditPackage();
        endpoints.MapGetCreditPurchase();
        endpoints.MapCreateCreditPackage();
        endpoints.MapUpdateCreditPackage();
        endpoints.MapGetSubscriptionPlans();
        endpoints.MapCreateSubscriptionCheckout();
        endpoints.MapGetMySubscription();
        endpoints.MapCancelSubscription();
        endpoints.MapValidatePromoCode();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Register this module's message handlers EXPLICITLY (conventional cross-assembly discovery is not
        // reliable for module assemblies). Billing publishes CreditsToppedUp / CreditsSpent via the outbox.
        options.Discovery.IncludeType<Messaging.ProvisionCreditAccountHandler>();
        options.Discovery.IncludeType<Messaging.ProcessStripeEventHandler>();
        // The canonical platform saga (EF-persisted in this module's DbContext, runs in the Worker).
        options.Discovery.IncludeType<CreditPurchaseSaga>();
    }

    public void RegisterJobs(IServiceCollectionQuartzConfigurator quartz, IConfiguration configuration)
    {
        // Cron sweep that materializes expired holds/buckets into the ledger (correctness is already live).
        var expireCron = configuration["Modules:Billing:Jobs:ExpireCreditsCron"] ?? "0 0 * * * ?"; // hourly
        var expireKey = new JobKey("billing-expire-credits");
        quartz.AddJob<Jobs.BillingExpireCreditsJob>(expireKey);
        quartz.AddTrigger(trigger => trigger.ForJob(expireKey).WithCronSchedule(expireCron));

        // Reconcile sweep: re-queues stuck stripe_events and corrects subscription drift (Stripe wins).
        var reconcileCron = configuration["Modules:Billing:Jobs:ReconcileStripeCron"] ?? "0 0 */6 * * ?";
        var reconcileKey = new JobKey("billing-stripe-reconcile");
        quartz.AddJob<Jobs.BillingStripeReconcileJob>(reconcileKey);
        quartz.AddTrigger(trigger => trigger.ForJob(reconcileKey).WithCronSchedule(reconcileCron));
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
