using System.Reflection;
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
}
