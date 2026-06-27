using ModularPlatform.Abstractions;
using ModularPlatform.Crm;
using ModularPlatform.Crm.Contracts;
using Shouldly;

namespace ModularPlatform.Crm.Tests;

/// <summary>
/// Phase 0 scaffolding smoke tests: the module exists, implements <see cref="IModule"/>, and its key matches the
/// flag/permission convention. Feature-level integration tests (over the shared <c>PlatformApiFactory</c>) arrive
/// with the Contacts feature.
/// </summary>
public sealed class CrmModuleTests
{
    [Fact]
    public void Module_is_an_IModule_named_Crm()
    {
        IModule module = new CrmModule();
        module.Name.ShouldBe("Crm");
    }

    [Fact]
    public void Module_key_matches_the_contracts_constant()
    {
        // "Crm" flag (Modules:Crm:Enabled) and "crm" RequireModule/permission key are intentionally case-distinct.
        new CrmModule().Name.ShouldBe("Crm");
        CrmContracts.ModuleKey.ShouldBe("crm");
    }
}
