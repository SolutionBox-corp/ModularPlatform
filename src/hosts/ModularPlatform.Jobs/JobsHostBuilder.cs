using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using ModularPlatform.Crm;
using ModularPlatform.Files;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Marketing;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
using ModularPlatform.Operations;
using ModularPlatform.Persistence;
using ModularPlatform.Realtime;
using ModularPlatform.Telemetry;
using ModularPlatform.Tenancy;
using Quartz;
using Wolverine;

namespace ModularPlatform.Jobs;

/// <summary>
/// Composes the Jobs (Quartz CRON) host. Extracted from <c>Program</c> so a boot test can build the exact same DI
/// graph the deployed host uses (validating, among others, that every job's command dependencies — incl. the
/// realtime/notification graph A4 worried about — are resolvable in a non-HTTP host).
/// </summary>
public static class JobsHostBuilder
{
    public static HostApplicationBuilder Create(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        ValidateSingleInstancePosture(builder.Configuration);

        // System context (no HTTP). Modules register their services so jobs can dispatch commands through IDispatcher.
        builder.Services.AddPlatformCore();
        builder.Services.AddPlatformTelemetry("ModularPlatform.Jobs", builder.Configuration, builder.Environment);
        // Command-pipeline parity with the Api host: jobs dispatch commands, so they get Logging + Validation too —
        // after Telemetry and before the modules' ConcurrencyRetry (order: Telemetry → Logging → Validation → Retry).
        builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        // A job may dispatch a command whose handler chains into a notification (SendNotificationHandler injects
        // IRealtimePublisher). Register the realtime publisher (Redis fan-out, or the local no-Redis fallback) so the
        // module graph is resolvable here exactly as in the Worker — without it the Jobs DI graph is unfulfillable.
        builder.Services.AddPlatformRealtime(builder.Configuration);

        var modules = ModuleLoader.Discover(
            builder.Configuration,
            typeof(IdentityModule).Assembly,
            typeof(BillingModule).Assembly,
            typeof(NotificationsModule).Assembly,
            typeof(GdprModule).Assembly,
            typeof(OperationsModule).Assembly,
            // Files has no cron jobs today, but the Jobs host loads the full module set (like every other host) so the
            // DI graph stays uniform and the host-boot test validates it — no special-case omission to drift on.
            typeof(FilesModule).Assembly,
            typeof(MarketingModule).Assembly,
            typeof(CrmModule).Assembly,
            typeof(TenancyModule).Assembly);
        foreach (var module in modules)
        {
            module.RegisterServices(builder.Services, builder.Configuration);
        }

        var conn = builder.Configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        // Wolverine wires the module DbContexts (EF outbox integration). This host LISTENS to nothing — it only
        // schedules jobs and PUBLISHES (e.g. the Stripe reconcile job outboxes ProcessStripeEventMessage /
        // CreditPurchaseConfirmed). Those published messages flow to the shared Postgres queue and are drained by the
        // Worker (THE consumer); a published-but-undrained envelope is recovered by the Worker's durability agent, and
        // the reconcile job is itself idempotent and re-runs on its cron, so a pure-publisher Balanced Jobs node is safe.
        builder.UseWolverine(opts => PlatformMessaging.Configure(opts, conn, modules));

        // Quartz = CRON ONLY (credit expiry, reconciliation, retention sweeps). Each module contributes its jobs.
        // Durable event-driven work belongs in the Worker host via Wolverine — never here.
        //
        // DEPLOYMENT: this uses Quartz's default IN-MEMORY job store. [DisallowConcurrentExecution] serializes a job
        // only WITHIN one scheduler instance — it does NOT coordinate across instances. Run the Jobs host as a SINGLE
        // instance (replica = 1). Every job here is idempotent (expiry/release use UNIQUE ledger keys, reconcile and
        // the retention sweep are date-guarded, the health check is read-only), so a duplicate run from a second
        // instance is safe but wasteful. If HA is required, switch to the clustered Quartz AdoJobStore (Postgres) —
        // do not simply scale the replica count.
        builder.Services.AddQuartz(quartz =>
        {
            foreach (var module in modules)
            {
                module.RegisterJobs(quartz, builder.Configuration);
            }

            // Platform-level messaging health check — not a module concern (reads Wolverine internals via IMessageStore).
            var healthCron = builder.Configuration["Messaging:HealthCheckCron"] ?? "0 0/5 * * * ?"; // every 5 min
            var healthKey = new JobKey("platform-messaging-health");
            quartz.AddJob<MessagingHealthJob>(healthKey);
            // Cron is interpreted in UTC (Law #7) — Quartz otherwise defaults to the host's local timezone.
            quartz.AddTrigger(trigger => trigger.ForJob(healthKey)
                .WithCronSchedule(healthCron, x => x.InTimeZone(TimeZoneInfo.Utc)));
        });
        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return builder;
    }

    private static void ValidateSingleInstancePosture(IConfiguration config)
    {
        var replicaCount = config.GetValue<int?>("Jobs:ReplicaCount") ?? 1;
        if (replicaCount < 1)
        {
            throw new InvalidOperationException("Jobs:ReplicaCount must be at least 1.");
        }

        if (replicaCount > 1)
        {
            throw new InvalidOperationException(
                "Jobs host uses Quartz in-memory store and must run with Jobs:ReplicaCount=1. "
                + "Use a clustered Quartz persistent store before scaling Jobs beyond one replica.");
        }
    }
}
