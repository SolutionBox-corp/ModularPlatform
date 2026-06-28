using System.Linq;
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
    private static readonly System.Reflection.Assembly[] ModuleCoreAssemblies =
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

    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(ModularPlatform.Cqrs.IDispatcher).Assembly,
            typeof(ModularPlatform.Abstractions.IModule).Assembly,
            ModuleCoreAssemblies[0],
            typeof(ModularPlatform.Identity.Contracts.UserRegisteredIntegrationEvent).Assembly,
            ModuleCoreAssemblies[1],
            typeof(ModularPlatform.Billing.Contracts.CreditsToppedUpIntegrationEvent).Assembly,
            ModuleCoreAssemblies[2],
            typeof(ModularPlatform.Notifications.Contracts.EmailDeliveryRequested).Assembly,
            ModuleCoreAssemblies[3],
            typeof(ModularPlatform.Gdpr.Contracts.UserErasureRequested).Assembly,
            ModuleCoreAssemblies[4],
            ModuleCoreAssemblies[5],
            typeof(ModularPlatform.Files.Contracts.LinkFileToOwnerCommand).Assembly,
            ModuleCoreAssemblies[6],
            ModuleCoreAssemblies[7],
            typeof(ModularPlatform.Tenancy.Contracts.TenantProvisionedIntegrationEvent).Assembly)
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

    private static readonly string[] Modules =
        ["Identity", "Billing", "Notifications", "Gdpr", "Operations", "Files", "Marketing", "Tenancy"];

    [Fact]
    public void Every_loaded_module_core_is_covered_by_the_boundary_matrix()
    {
        var loadedModuleNames = ModuleCoreAssemblies
            .Select(a => a.GetName().Name!.Split('.')[1])
            .ToList();

        var missing = loadedModuleNames.Except(Modules).ToList();
        Assert.True(missing.Count == 0,
            "A module assembly is loaded but missing from the boundary matrix: " + string.Join(", ", missing));
    }

    [Fact]
    public void No_module_core_depends_on_another_modules_core()
    {
        // A module's Core is internal; other modules talk to it ONLY via its Contracts (reference by Id, never a
        // cross-module JOIN or Core type). This guards the seam that lets a module be extracted into its own service.
        // Covers EVERY module (not just Identity) and matches NESTED namespaces (…\.Features\.X), not only the root.
        foreach (var module in Modules)
        {
            var others = Modules.Where(m => m != module);
            var otherCores = Types().That()
                .ResideInNamespaceMatching($@"^ModularPlatform\.({string.Join("|", others)})(\..*)?$")
                .And().DoNotResideInNamespaceMatching(@"^ModularPlatform\.\w+\.Contracts(\..*)?$");

            Types().That()
                .ResideInNamespaceMatching($@"^ModularPlatform\.{module}(\..*)?$")
                .And().DoNotResideInNamespaceMatching($@"^ModularPlatform\.{module}\.Contracts(\..*)?$")
                .Should().NotDependOnAny(otherCores)
                .Check(Architecture);
        }
    }
}
