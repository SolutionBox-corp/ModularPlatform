using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using ModularPlatform.Files;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
using ModularPlatform.Operations;
using ModularPlatform.Persistence;
using ModularPlatform.Realtime;
using ModularPlatform.Telemetry;
using ModularPlatform.Tenancy;
using Wolverine;

namespace ModularPlatform.Worker;

/// <summary>
/// Composes the Worker host. Extracted from <c>Program</c> so a boot test can build the exact same DI graph and
/// Wolverine configuration the deployed host uses (the Api integration harness cannot — it is a WebApplication of
/// the Api host, and the process-wide PII protector forbids a second host in the same process).
/// </summary>
public static class WorkerHostBuilder
{
    public static HostApplicationBuilder Create(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        // System context (no HTTP). Modules register their handlers + DbContext (Wolverine EF outbox).
        builder.Services.AddPlatformCore();
        builder.Services.AddPlatformTelemetry("ModularPlatform.Worker");
        // Command pipeline parity with the Api host: the Worker dispatches commands too (from integration-event
        // handlers), so it needs Logging + Validation just like the HTTP host — registered AFTER Telemetry and
        // BEFORE the modules' ConcurrencyRetry so the order stays Telemetry → Logging → Validation → ConcurrencyRetry.
        builder.Services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        builder.Services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        builder.Services.AddPlatformRealtime(builder.Configuration);

        var modules = ModuleLoader.Discover(
            builder.Configuration,
            typeof(IdentityModule).Assembly,
            typeof(BillingModule).Assembly,
            typeof(NotificationsModule).Assembly,
            typeof(GdprModule).Assembly,
            typeof(OperationsModule).Assembly,
            typeof(FilesModule).Assembly,
            typeof(TenancyModule).Assembly);
        foreach (var module in modules)
        {
            module.RegisterServices(builder.Services, builder.Configuration);
        }

        var conn = builder.Configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");

        // This host LISTENS on the durable Postgres queues and runs dispatched commands + integration-event handlers.
        // Solo by default (single Worker). Scale to multiple coordinated Workers by setting Messaging:SoloMode=false.
        var soloMode = builder.Configuration.GetValue("Messaging:SoloMode", true);
        // The Worker is THE consumer: it listens on the shared queue and runs handlers out of the publishing process.
        builder.UseWolverine(opts => PlatformMessaging.Configure(opts, conn, modules, soloMode, listen: true));

        return builder;
    }
}
