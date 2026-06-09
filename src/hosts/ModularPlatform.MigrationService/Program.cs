using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddPlatformCore();

var modules = ModuleLoader.Discover(
    builder.Configuration,
    typeof(IdentityModule).Assembly,
    typeof(BillingModule).Assembly,
    typeof(NotificationsModule).Assembly,
    typeof(GdprModule).Assembly);
foreach (var module in modules)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

var conn = builder.Configuration.GetConnectionString("Write")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
// Solo mode: we only need the module DbContexts wired; no message listeners.
builder.UseWolverine(opts => PlatformMessaging.Configure(opts, conn, modules));

var host = builder.Build();

// Apply every module's migrations, then provision RLS, then exit. Run before the Api/Worker serve traffic.
// RLS MUST be bootstrapped here too (not only in the Api startup path) — a deploy that runs this dedicated
// MigrationService and turns OFF the Api's RunMigrationsAtStartup would otherwise leave new IUserOwned tables
// with no row-level policy. Idempotent; no-op when Persistence:Rls:Enabled=false.
foreach (var module in modules)
{
    await module.ApplyMigrationsAsync(host.Services, CancellationToken.None);
}

await RlsBootstrapper.ApplyAsync(host.Services, conn, CancellationToken.None);
