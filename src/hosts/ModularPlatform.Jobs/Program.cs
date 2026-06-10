using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Jobs;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
using ModularPlatform.Operations;
using ModularPlatform.Persistence;
using ModularPlatform.Telemetry;
using Quartz;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);

// System context (no HTTP). Modules register their services so jobs can dispatch commands through IDispatcher.
builder.Services.AddPlatformCore();
builder.Services.AddPlatformTelemetry("ModularPlatform.Jobs");

var modules = ModuleLoader.Discover(
    builder.Configuration,
    typeof(IdentityModule).Assembly,
    typeof(BillingModule).Assembly,
    typeof(NotificationsModule).Assembly,
    typeof(GdprModule).Assembly,
    typeof(OperationsModule).Assembly);
foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

var conn = builder.Configuration.GetConnectionString("Write")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
// Wolverine wires the module DbContexts (EF outbox integration); this host listens to nothing, it only schedules.
builder.UseWolverine(opts => PlatformMessaging.Configure(opts, conn, modules));

// Quartz = CRON ONLY (credit expiry, reconciliation, retention sweeps). Each module contributes its jobs.
// Durable event-driven work belongs in the Worker host via Wolverine — never here.
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
    quartz.AddTrigger(trigger => trigger.ForJob(healthKey).WithCronSchedule(healthCron));
});
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

var host = builder.Build();
host.Run();
