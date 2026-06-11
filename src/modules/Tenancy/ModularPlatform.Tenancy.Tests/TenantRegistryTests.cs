using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// The Tenancy module owns the tenant registry (moved out of Identity). Registration provisions a registry row
/// through the <c>ITenantProvisioning</c> port: a unique subdomain, an Active status, and the user stamped to it.
/// </summary>
[Collection("Integration")]
public sealed class TenantRegistryTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Registration_provisions_a_registry_row_with_a_subdomain_and_active_status()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"reg-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var tenantId = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
        tenantId.ShouldNotBe(Guid.Empty);

        var subdomain = await fixture.ScalarAsync<string>(
            $"SELECT \"Subdomain\" FROM tenants WHERE \"Id\" = '{tenantId}'");
        subdomain.ShouldNotBeNullOrWhiteSpace();

        var status = await fixture.ScalarAsync<string>($"SELECT \"Status\" FROM tenants WHERE \"Id\" = '{tenantId}'");
        status.ShouldBe("Active");
    }

    [Fact]
    public async Task Each_registration_gets_a_distinct_tenant_with_a_distinct_subdomain()
    {
        var (userA, _) = await fixture.RegisterAndLoginAsync($"ra-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var (userB, _) = await fixture.RegisterAndLoginAsync($"rb-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var tenantA = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userA}'");
        var tenantB = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userB}'");
        tenantA.ShouldNotBe(tenantB);

        var subA = await fixture.ScalarAsync<string>($"SELECT \"Subdomain\" FROM tenants WHERE \"Id\" = '{tenantA}'");
        var subB = await fixture.ScalarAsync<string>($"SELECT \"Subdomain\" FROM tenants WHERE \"Id\" = '{tenantB}'");
        subA.ShouldNotBe(subB);
    }
}
