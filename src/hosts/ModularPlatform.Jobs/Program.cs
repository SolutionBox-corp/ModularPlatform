using ModularPlatform.Telemetry;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPlatformTelemetry("ModularPlatform.Jobs");

// Quartz = CRON ONLY (reconciliation, retention sweeps, credit expiry, stuck-outbox alerting).
// Durable event-driven work belongs in the Worker host via Wolverine — never here.
builder.Services.AddQuartz();
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// Modules register their recurring jobs here as they are added (e.g. Billing reconciliation).

var host = builder.Build();
host.Run();
