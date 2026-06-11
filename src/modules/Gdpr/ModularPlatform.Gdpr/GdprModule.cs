using FluentValidation;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Gdpr.Features.Consents.GetConsents;
using ModularPlatform.Gdpr.Features.Consents.GrantConsent;
using ModularPlatform.Gdpr.Features.Consents.WithdrawConsent;
using ModularPlatform.Gdpr.Features.Erasure.RequestErasure;
using ModularPlatform.Gdpr.Features.Export.ExportUserData;
using ModularPlatform.Gdpr.Jobs;
using ModularPlatform.Gdpr.Persistence;
using ModularPlatform.Gdpr.Security;
using ModularPlatform.Messaging;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Rls;
using Quartz;
using Wolverine;

namespace ModularPlatform.Gdpr;

/// <summary>
/// GDPR orchestrator module. Boundary-clean: it depends ONLY on the Abstractions ports
/// (<see cref="IExportPersonalData"/>, <see cref="IErasePersonalData"/>) and never on another module's Core.
/// It fans portability/erasure across whichever modules implement those ports, owns the consent log and the
/// per-subject crypto-shredding key envelope, and publishes <c>UserErasureRequested</c> via the outbox.
/// </summary>
public sealed class GdprModule : IModule
{
    public string Name => "Gdpr";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var write = configuration.GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        var read = configuration.GetConnectionString("Read") ?? write;

        services.AddCqrs(typeof(GdprModule).Assembly);
        services.AddValidatorsFromAssembly(typeof(GdprModule).Assembly, includeInternalTypes: true);

        services.AddModuleDbContext<GdprDbContext>(Name, write);
        services.AddModuleReadDbContext<GdprDbContext>(read);

        // The audit interceptor (a platform singleton) crypto-shreds PII through this port. The protector runs on
        // its OWN system-context GdprDbContext (no audit interceptor -> no reentrancy) so it can manage subject_keys
        // for any subject. Registered as a singleton to match the singleton interceptor.
        services.AddSingleton<IPersonalDataProtector>(sp =>
        {
            var rls = sp.GetRequiredService<IOptions<RlsOptions>>().Value;
            // MUST be the WRITE primary: GetOrCreateDek INSERTs subject_keys (the first PII write for a new
            // subject), which fails on a read replica; and the "live DEK read" guarantee (a just-shredded key must
            // read back as shredded) would be violated by replica lag. The protector context is low-traffic
            // single-row PK lookups, so it gains nothing from the replica anyway.
            var runtimeConnectionString = RlsConnectionString.ForRuntime(write, rls);
            var system = new SystemTenantContext();

            GdprDbContext NewContext()
            {
                var builder = new DbContextOptionsBuilder<GdprDbContext>().UseNpgsql(runtimeConnectionString);
                if (rls.Enabled)
                {
                    builder.AddInterceptors(new PrincipalSessionConnectionInterceptor(system));
                }

                return new GdprDbContext(builder.Options, system);
            }

            return new PersonalDataProtector(NewContext, sp.GetRequiredService<IClock>());
        });

        // Blind-index hashing for lookups on encrypted columns (e.g. users.EmailHash). Platform-wide secret
        // key, fail-fast outside Development — same posture as the JWT signing key.
        services.AddOptions<GdprEncryptionOptions>()
            .BindConfiguration(GdprEncryptionOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GdprEncryptionOptions>, GdprEncryptionOptionsValidator>();
        services.AddSingleton<IBlindIndexHasher, HmacBlindIndexHasher>();

        // The Gdpr module owns the consent log — it must participate in its OWN export/erasure fan-out, or a subject's
        // consent history (their personal data) would be missing from the export and survive erasure.
        services.AddScoped<IExportPersonalData, Features.Consents.ConsentPersonalDataExporter>();
        services.AddScoped<IErasePersonalData, Features.Consents.ConsentPersonalDataEraser>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapExportUserData();
        endpoints.MapRequestErasure();
        endpoints.MapGrantConsent();
        endpoints.MapWithdrawConsent();
        endpoints.MapGetConsents();
    }

    public void ConfigureMessaging(WolverineOptions options)
    {
        // Register this module's message handlers explicitly (cross-assembly conventional discovery is unreliable).
        options.Discovery.IncludeType<Messaging.UserErasureRequestedHandler>();
    }

    public void RegisterJobs(IServiceCollectionQuartzConfigurator quartz, IConfiguration configuration)
    {
        // Nightly retention sweep. NOTE: shredded subject_key tombstones are retained PERMANENTLY as the DEK re-mint
        // guard, so this sweep currently purges nothing (see RetentionSweepCommand) — it's the seam for future
        // module-owned purgeable retention data.
        var cron = configuration["Modules:Gdpr:Jobs:RetentionSweepCron"] ?? "0 0 3 * * ?"; // 03:00 UTC daily
        var jobKey = new JobKey("gdpr-retention-sweep");
        quartz.AddJob<GdprRetentionSweepJob>(jobKey);
        // Cron is interpreted in UTC (Law #7) — Quartz otherwise defaults to the host's local timezone, firing the
        // "03:00 UTC" sweep at the wrong wall-clock time on any host not pinned to UTC.
        quartz.AddTrigger(trigger => trigger.ForJob(jobKey)
            .WithCronSchedule(cron, x => x.InTimeZone(TimeZoneInfo.Utc)));
    }

    public async Task ApplyMigrationsAsync(IServiceProvider services, CancellationToken ct)
    {
        // Migrations run on the ADMIN connection — the DI-registered context uses the RLS runtime role,
        // which cannot run DDL. The RLS bootstrapper (host, post-migration) then provisions role + policies.
        var adminConnectionString = services.GetRequiredService<IConfiguration>().GetConnectionString("Write")
            ?? throw new InvalidOperationException("Missing ConnectionStrings:Write");
        await PlatformMigrator.MigrateAsync<GdprDbContext>(services, adminConnectionString, Name, ct);
    }
}
