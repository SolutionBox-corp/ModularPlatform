using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
using ModularPlatform.Operations;
using ModularPlatform.Persistence.Rls;
using ModularPlatform.Realtime;
using ModularPlatform.Telemetry;
using ModularPlatform.Web;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

// Discover enabled modules ONCE; the same list drives services, endpoints and messaging.
var modules = ModuleLoader.Discover(
    builder.Configuration,
    typeof(IdentityModule).Assembly,
    typeof(BillingModule).Assembly,
    typeof(NotificationsModule).Assembly,
    typeof(GdprModule).Assembly,
    typeof(OperationsModule).Assembly);

// Platform cross-cutting concerns. Telemetry FIRST so its behavior is outer-most in the CQRS pipeline.
builder.Services.AddPlatformTelemetry("ModularPlatform.Api");
builder.Services.AddPlatformWeb(builder.Configuration);
builder.Services.AddPlatformRealtime(builder.Configuration);
builder.Services.AddOpenApi();

var writeConn = builder.Configuration.GetConnectionString("Write")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
builder.Services.AddHealthChecks().AddNpgSql(writeConn, name: "postgres", tags: ["ready"]);

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

// Durable messaging: Postgres outbox/inbox transport; each module contributes its handlers/routes.
// Solo durability when the Api is the only node (tests, single-instance deploy) so the durable-queue agent
// drains immediately. With a dedicated Worker scaled out, set Messaging:SoloMode=false on both for Balanced.
var soloMode = builder.Configuration.GetValue("Messaging:SoloMode", builder.Environment.IsEnvironment("Testing"));
builder.UseWolverine(opts => PlatformMessaging.Configure(opts, writeConn, modules, soloMode));

var app = builder.Build();

// Dev/test convenience: apply every module's migrations on boot when enabled, then provision RLS.
if (builder.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
{
    foreach (var module in modules)
    {
        await module.ApplyMigrationsAsync(app.Services, CancellationToken.None);
    }

    // After the tables exist: provision the least-privilege runtime role + grants and apply the row-level
    // security policies on every IUserOwned table. Idempotent; no-op when Persistence:Rls:Enabled=false.
    await RlsBootstrapper.ApplyAsync(app.Services, writeConn, CancellationToken.None);
}

app.UsePlatformWeb();
app.MapOpenApi();

// API versioning (URL strategy): every module/app endpoint is served under a single central `/v1` prefix
// (e.g. /v1/identity/users, /v1/billing/..., /v1/realtime/stream). Health checks and OpenAPI stay UNVERSIONED
// at root. Bump by adding a `/v2` group here when a breaking revision is needed.
var v1 = app.MapGroup("/v1");

foreach (var module in modules)
{
    module.MapEndpoints(v1);
}

// Browser Server-Sent-Events stream (realtime fan-out is the Realtime building block; producers stay agnostic).
v1.MapRealtimeStream();

// Liveness = process is up (no dependency checks); readiness = dependencies (Postgres) are reachable.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();

app.Run();

/// <summary>Exposed so integration tests can spin up the host with WebApplicationFactory.</summary>
public partial class Program;
