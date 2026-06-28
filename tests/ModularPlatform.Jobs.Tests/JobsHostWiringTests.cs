using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ModularPlatform.Jobs;
using Quartz;
using Shouldly;

namespace ModularPlatform.Jobs.Tests;

public sealed class JobsHostWiringTests
{
    [Fact]
    public void Platform_messaging_health_job_disallows_concurrent_execution()
    {
        var jobType = typeof(JobsHostBuilder).Assembly
            .GetType("ModularPlatform.Jobs.MessagingHealthJob", throwOnError: true);

        jobType.ShouldNotBeNull();
        jobType.GetCustomAttribute<DisallowConcurrentExecutionAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void Jobs_host_waits_for_running_jobs_on_shutdown()
    {
        using var host = JobsHostBuilder.Create(BootArgs()).Build();

        var options = host.Services.GetRequiredService<IOptions<QuartzHostedServiceOptions>>().Value;

        options.WaitForJobsToComplete.ShouldBeTrue();
    }

    private static string[] BootArgs() =>
    [
        "--environment=Development",
        "--ConnectionStrings:Write=Host=localhost;Port=5432;Database=jobswiring;Username=postgres;Password=postgres",
        "--ConnectionStrings:Read=Host=localhost;Port=5432;Database=jobswiring;Username=postgres;Password=postgres",
        "--Modules:Identity:Enabled=true",
        "--Modules:Billing:Enabled=true",
        "--Modules:Notifications:Enabled=true",
        "--Modules:Gdpr:Enabled=true",
        "--Modules:Operations:Enabled=true",
        "--Modules:Files:Enabled=true",
        "--Modules:Marketing:Enabled=true",
        "--Modules:Tenancy:Enabled=true",
        "--Billing:Stripe:UseFakeGateway=true",
        "--Storage:Provider=local",
    ];
}
