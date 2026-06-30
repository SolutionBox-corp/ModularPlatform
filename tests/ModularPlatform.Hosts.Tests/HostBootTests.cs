using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Cqrs.Behaviors;
using ModularPlatform.Identity;
using ModularPlatform.Jobs;
using ModularPlatform.MigrationService;
using ModularPlatform.Persistence;
using ModularPlatform.Persistence.Behaviors;
using ModularPlatform.Telemetry;
using ModularPlatform.Worker;
using Npgsql;
using Shouldly;
using Wolverine;

namespace ModularPlatform.Hosts.Tests;

/// <summary>
/// Boots the non-HTTP hosts the Api integration harness can never reach (Worker, Jobs). We BUILD each host — not
/// Start — which, in the Development environment, runs <c>ServiceProvider</c> validation (ValidateOnBuild +
/// ValidateScopes): every registered dependency must be resolvable and no scoped service may be captured by a
/// singleton. This is the regression guard for the host-composition/DI-graph concerns (A4) that the Api-only
/// harness could not cover. We deliberately do NOT call StartAsync: no DB/Wolverine connection is needed, and not
/// starting means the process-wide PII protector is never set, so this assembly stays a clean separate process
/// from the Api integration tests (the one-host-per-process invariant). Full transport start-up is a staging check.
/// </summary>
public sealed class HostBootTests
{
    private const string WriteConnectionString =
        "Host=localhost;Port=5432;Database=hostboot;Username=postgres;Password=postgres";

    // Minimal config supplied as command-line args (no appsettings.json on the test content root): a syntactically
    // valid (never-connected) write/read connection + every module enabled, so the FULL module graph is validated.
    private static string[] BootArgs(string? readConnectionString = WriteConnectionString)
    {
        var args = new List<string>
        {
            "--environment=Development",
            "--ConnectionStrings:Write",
            WriteConnectionString,
            "--RunMigrationsAtStartup=false",
            "--Modules:Identity:Enabled=true",
            "--Modules:Billing:Enabled=true",
            "--Modules:Notifications:Enabled=true",
            "--Modules:Gdpr:Enabled=true",
            "--Modules:Operations:Enabled=true",
            "--Modules:Files:Enabled=true",
            "--Modules:Marketing:Enabled=true",
            "--Modules:Tenancy:Enabled=true",
            // The fake Stripe gateway is exempt from the prod guard in Development and avoids needing a real API key.
            "--Billing:Stripe:UseFakeGateway=true",
            "--Jwt:Issuer=test",
            "--Jwt:Audience=test",
            "--Jwt:SigningKey=host-boot-signing-key-at-least-32b",
            "--Secrets:MasterKeys:1=aG9zdC1ib290LXNlY3JldHMta2V5LTAwMDAwMDAwMDA=",
            "--Storage:Provider=local",
        };

        if (readConnectionString is not null)
        {
            args.Add("--ConnectionStrings:Read");
            args.Add(readConnectionString);
        }

        return [.. args];
    }

    [Fact]
    public async Task Api_host_composes_and_its_dependency_graph_is_valid()
    {
        await using var app = await ApiHostBuilder.CreateAsync(BootArgs());

        app.ShouldNotBeNull();
        AssertPiiRetention(app);
    }

    [Fact]
    public void Worker_host_composes_and_its_dependency_graph_is_valid()
    {
        using var host = WorkerHostBuilder.Create(BootArgs()).Build();

        host.ShouldNotBeNull();
        // The Worker is the consumer: PII-minimizing durable retention must be in effect.
        AssertPiiRetention(host);
    }

    [Fact]
    public void Jobs_host_composes_and_its_dependency_graph_is_valid()
    {
        using var host = JobsHostBuilder.Create(BootArgs()).Build();

        host.ShouldNotBeNull();
        AssertPiiRetention(host);
    }

    [Fact]
    public void Worker_and_jobs_hosts_register_command_pipeline_behaviors_in_the_expected_order()
    {
        AssertCommandPipelineBehaviorOrder(WorkerHostBuilder.Create(BootArgs()).Services);
        AssertCommandPipelineBehaviorOrder(JobsHostBuilder.Create(BootArgs()).Services);
    }

    [Fact]
    public void Non_http_hosts_run_with_system_tenant_context()
    {
        using var worker = WorkerHostBuilder.Create(BootArgs()).Build();
        using var jobs = JobsHostBuilder.Create(BootArgs()).Build();
        using var migration = MigrationHostBuilder.Create(BootArgs(), out _).Build();

        AssertSystemTenantContext(worker);
        AssertSystemTenantContext(jobs);
        AssertSystemTenantContext(migration);
    }

    [Fact]
    public void MigrationService_host_composes_and_its_dependency_graph_is_valid()
    {
        using var host = MigrationHostBuilder.Create(BootArgs(), out var modules).Build();

        host.ShouldNotBeNull();
        modules.ShouldNotBeEmpty();
        AssertPiiRetention(host);
    }

    [Fact]
    public void Module_read_context_falls_back_to_write_connection_when_no_read_replica_is_configured()
    {
        using var host = WorkerHostBuilder.Create(BootArgs(readConnectionString: " ")).Build();

        host.Services.GetRequiredService<IConfiguration>().GetConnectionString("Write").ShouldBe(WriteConnectionString);

        var identityDbContextType = typeof(IdentityModule).Assembly.GetType(
            "ModularPlatform.Identity.Persistence.IdentityDbContext", throwOnError: true)!;
        var factoryType = typeof(IReadDbContextFactory<>).MakeGenericType(identityDbContextType);
        var factory = host.Services.GetRequiredService(factoryType);
        using var db = (DbContext)factoryType.GetMethod(nameof(IReadDbContextFactory<PlatformDbContext>.Create))!
            .Invoke(factory, null)!;

        var write = new NpgsqlConnectionStringBuilder(WriteConnectionString);
        var read = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());

        read.Host.ShouldBe(write.Host);
        read.Port.ShouldBe(write.Port);
        read.Database.ShouldBe(write.Database);
    }

    private static void AssertPiiRetention(IHost host)
    {
        var options = host.Services.GetRequiredService<WolverineOptions>();
        options.Durability.DeadLetterQueueExpirationEnabled.ShouldBeTrue();
        options.Durability.DeadLetterQueueExpiration.ShouldBe(TimeSpan.FromDays(7));
        options.Durability.KeepAfterMessageHandling.ShouldBe(TimeSpan.FromMinutes(5));
    }

    private static void AssertSystemTenantContext(IHost host)
    {
        var tenant = host.Services.GetRequiredService<ITenantContext>();
        tenant.ShouldBeOfType<SystemTenantContext>();
        tenant.IsSystem.ShouldBeTrue();
        tenant.UserId.ShouldBeNull();
        tenant.TenantId.ShouldBeNull();
    }

    private static void AssertCommandPipelineBehaviorOrder(IServiceCollection services)
    {
        var behaviors = services
            .Where(d => d.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(d => d.ImplementationType)
            .ToArray();

        behaviors.ShouldBe([
            typeof(TelemetryBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(ValidationBehavior<,>),
            typeof(ConcurrencyRetryBehavior<,>)
        ]);
    }
}
