using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
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
    typeof(GdprModule).Assembly);

// Platform cross-cutting concerns. Telemetry FIRST so its behavior is outer-most in the CQRS pipeline.
builder.Services.AddPlatformTelemetry("ModularPlatform.Api");
builder.Services.AddPlatformWeb(builder.Configuration);
builder.Services.AddPlatformRealtime(builder.Configuration);
builder.Services.AddOpenApi();

foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

// Durable messaging: Postgres outbox/inbox transport; each module contributes its handlers/routes.
var messagingConn = builder.Configuration.GetConnectionString("Write")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
// Solo durability when the Api is the only node (tests, single-instance deploy) so the durable-queue agent
// drains immediately. With a dedicated Worker scaled out, set Messaging:SoloMode=false on both for Balanced.
var soloMode = builder.Configuration.GetValue("Messaging:SoloMode", builder.Environment.IsEnvironment("Testing"));
builder.UseWolverine(opts => PlatformMessaging.Configure(opts, messagingConn, modules, soloMode));

var app = builder.Build();

// Dev/test convenience: apply every module's migrations on boot when enabled.
if (builder.Configuration.GetValue<bool>("RunMigrationsAtStartup"))
{
    foreach (var module in modules)
    {
        await module.ApplyMigrationsAsync(app.Services, CancellationToken.None);
    }
}

app.UsePlatformWeb();
app.MapOpenApi();

foreach (var module in modules)
{
    module.MapEndpoints(app);
}

app.MapGet("/health", () => Results.Ok(new { status = "healthy" })).AllowAnonymous();

app.Run();

/// <summary>Exposed so integration tests can spin up the host with WebApplicationFactory.</summary>
public partial class Program;
