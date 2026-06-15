using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModularPlatform.Jobs;
using ModularPlatform.MigrationService;
using ModularPlatform.Worker;
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
    // Minimal config supplied as command-line args (no appsettings.json on the test content root): a syntactically
    // valid (never-connected) write/read connection + every module enabled, so the FULL module graph is validated.
    private static string[] BootArgs() =>
    [
        "--environment=Development",
        "--ConnectionStrings:Write=Host=localhost;Port=5432;Database=hostboot;Username=postgres;Password=postgres",
        "--ConnectionStrings:Read=Host=localhost;Port=5432;Database=hostboot;Username=postgres;Password=postgres",
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
    ];

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

    private static void AssertPiiRetention(IHost host)
    {
        var options = host.Services.GetRequiredService<WolverineOptions>();
        options.Durability.DeadLetterQueueExpirationEnabled.ShouldBeTrue();
        options.Durability.DeadLetterQueueExpiration.ShouldBe(TimeSpan.FromDays(7));
        options.Durability.KeepAfterMessageHandling.ShouldBe(TimeSpan.FromMinutes(5));
    }
}
