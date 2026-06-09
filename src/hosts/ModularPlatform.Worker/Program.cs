using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
using ModularPlatform.Operations;
using ModularPlatform.Persistence;
using ModularPlatform.Realtime;
using ModularPlatform.Telemetry;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);

// System context (no HTTP). Modules register their handlers + DbContext (Wolverine EF outbox).
builder.Services.AddPlatformCore();
builder.Services.AddPlatformTelemetry("ModularPlatform.Worker");
builder.Services.AddPlatformRealtime(builder.Configuration);

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

// This host LISTENS on the durable Postgres queues and runs dispatched commands + integration-event handlers.
// Solo by default (single Worker). Scale to multiple coordinated Workers by setting Messaging:SoloMode=false.
var soloMode = builder.Configuration.GetValue("Messaging:SoloMode", true);
builder.UseWolverine(opts => PlatformMessaging.Configure(opts, conn, modules, soloMode));

var host = builder.Build();
host.Run();
