using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ModularPlatform.ArchitectureTests;

/// <summary>
/// Enforces the platform's module-boundary law at build time. As modules are added, add their
/// assemblies to <see cref="Architecture"/>; each new module is then covered by the cross-module rules.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(ModularPlatform.Cqrs.IDispatcher).Assembly,
            typeof(ModularPlatform.Abstractions.IModule).Assembly,
            typeof(ModularPlatform.Identity.IdentityModule).Assembly,
            typeof(ModularPlatform.Identity.Contracts.UserRegisteredIntegrationEvent).Assembly,
            typeof(ModularPlatform.Billing.BillingModule).Assembly,
            typeof(ModularPlatform.Billing.Contracts.CreditsToppedUpIntegrationEvent).Assembly,
            typeof(ModularPlatform.Notifications.NotificationsModule).Assembly,
            typeof(ModularPlatform.Notifications.Contracts.EmailDeliveryRequested).Assembly,
            typeof(ModularPlatform.Gdpr.GdprModule).Assembly,
            typeof(ModularPlatform.Gdpr.Contracts.UserErasureRequested).Assembly,
            typeof(ModularPlatform.Operations.OperationsModule).Assembly,
            typeof(ModularPlatform.Files.FilesModule).Assembly)
        .Build();

    [Fact]
    public void Contracts_assemblies_must_not_depend_on_infrastructure()
    {
        // *.Contracts is the only public surface modules share — it must stay pure (DTOs/events + Cqrs).
        Types().That()
            .ResideInNamespaceMatching(@"ModularPlatform\..*\.Contracts")
            .Should().NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(@"ModularPlatform\.(Persistence|Messaging|Web)"))
            .Check(Architecture);
    }

    [Fact]
    public void Identity_core_must_not_depend_on_other_module_cores()
    {
        // A module's Core is internal; other modules talk to it ONLY via its Contracts. This guards the seam
        // that lets a module be extracted into its own service later.
        Types().That()
            .ResideInNamespaceMatching(@"ModularPlatform\.Identity")
            .And().DoNotResideInNamespaceMatching(@"ModularPlatform\.Identity\.Contracts")
            .Should().NotDependOnAny(Types().That()
                .ResideInNamespaceMatching(@"ModularPlatform\.(Billing|Notifications|Audit|Gdpr)$"))
            .Check(Architecture);
    }
}
