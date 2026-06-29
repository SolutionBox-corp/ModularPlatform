using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Identity;
using ModularPlatform.Jobs;
using ModularPlatform.MigrationService;
using ModularPlatform.Persistence;
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
}
