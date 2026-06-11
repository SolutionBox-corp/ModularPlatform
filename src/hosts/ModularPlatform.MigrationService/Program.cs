using ModularPlatform.MigrationService;
using ModularPlatform.Persistence.Rls;

var builder = MigrationHostBuilder.Create(args, out var modules);

var conn = builder.Configuration.GetConnectionString("Write")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");

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
