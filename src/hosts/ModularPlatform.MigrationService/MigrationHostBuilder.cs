using ModularPlatform.Abstractions;
using ModularPlatform.Billing;
using ModularPlatform.Files;
using ModularPlatform.Gdpr;
using ModularPlatform.Identity;
using ModularPlatform.Messaging;
using ModularPlatform.Notifications;
using ModularPlatform.Operations;
using ModularPlatform.Persistence;
using ModularPlatform.Realtime;
using Wolverine;

namespace ModularPlatform.MigrationService;

/// <summary>
/// Composes the migration host (applies every module's migrations + bootstraps RLS, then exits). Extracted from
/// <c>Program</c> so a boot test validates this host's DI graph too — it registers the SAME module services as the
/// Worker (incl. the notification graph that needs <see cref="IRealtimePublisher"/>), so it must register the
/// realtime publisher or its graph is unfulfillable (latent because ValidateOnBuild is off outside Development).
/// </summary>
public static class MigrationHostBuilder
{
    public static HostApplicationBuilder Create(string[] args, out IReadOnlyList<IModule> modules)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Services.AddPlatformCore();
        // Same module graph as the other hosts → register the realtime publisher so the graph stays resolvable
        // (the Notifications handlers inject IRealtimePublisher). Local fallback when no Redis is configured.
        builder.Services.AddPlatformRealtime(builder.Configuration);

        var discovered = ModuleLoader.Discover(
            builder.Configuration,
            typeof(IdentityModule).Assembly,
            typeof(BillingModule).Assembly,
            typeof(NotificationsModule).Assembly,
            typeof(GdprModule).Assembly,
            typeof(OperationsModule).Assembly,
            typeof(FilesModule).Assembly);
        foreach (var module in discovered)
        {
            module.RegisterServices(builder.Services, builder.Configuration);
        }

        var conn = builder.Configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        // Solo mode: we only need the module DbContexts wired; no message listeners.
        builder.UseWolverine(opts => PlatformMessaging.Configure(opts, conn, discovered));

        modules = discovered;
        return builder;
    }
}
