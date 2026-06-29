using System;
using System.Linq;
using System.Reflection;
using ModularPlatform.Cqrs;
using Quartz;
using Xunit;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// UC100: module jobs are scheduler adapters only. Business logic belongs in the command handler they dispatch.
/// </summary>
public sealed class ModuleJobArchitectureTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(ModularPlatform.Identity.IdentityModule).Assembly,
        typeof(ModularPlatform.Billing.BillingModule).Assembly,
        typeof(ModularPlatform.Notifications.NotificationsModule).Assembly,
        typeof(ModularPlatform.Gdpr.GdprModule).Assembly,
        typeof(ModularPlatform.Operations.OperationsModule).Assembly,
        typeof(ModularPlatform.Files.FilesModule).Assembly,
        typeof(ModularPlatform.Marketing.MarketingModule).Assembly,
        typeof(ModularPlatform.Tenancy.TenancyModule).Assembly,
    ];

    [Fact]
    public void Module_quartz_jobs_are_thin_non_overlapping_dispatcher_adapters()
    {
        var jobTypes = ModuleAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IJob).IsAssignableFrom(t))
            .ToList();

        Assert.True(jobTypes.Count > 0, "Expected at least one module Quartz job to lock the UC100 pattern.");

        var missingNoOverlap = jobTypes
            .Where(t => t.GetCustomAttribute<DisallowConcurrentExecutionAttribute>() is null)
            .Select(t => t.FullName)
            .ToList();
        Assert.True(missingNoOverlap.Count == 0,
            "Module Quartz jobs must use [DisallowConcurrentExecution] to avoid overlapping sweeps: "
            + string.Join(", ", missingNoOverlap));

        var nonThinConstructors = jobTypes
            .Where(t => t.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(ctor =>
                {
                    var parameters = ctor.GetParameters();
                    return parameters.Length != 1 || parameters[0].ParameterType != typeof(IDispatcher);
                }))
            .Select(t => t.FullName)
            .ToList();
        Assert.True(nonThinConstructors.Count == 0,
            "Module Quartz jobs should inject only IDispatcher; put dependencies/business logic in the command handler: "
            + string.Join(", ", nonThinConstructors));
    }
}
