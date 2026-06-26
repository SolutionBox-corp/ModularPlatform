using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// Platform-admin control plane: an admin (gated by <c>platform.tenants.manage</c>) provisions tenants and toggles
/// their module entitlements; a regular user cannot. Entitlement writes are cross-tenant by explicit Id (the registry
/// is not tenant-scoped).
/// </summary>
[Collection("Integration")]
public sealed class PlatformAdminTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    private async Task<string> AdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task Admin_provisions_a_tenant_then_toggles_its_entitlement()
    {
        var admin = await AdminTokenAsync();
        var subdomain = $"acme-{Guid.CreateVersion7():N}".Substring(0, 30);

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Acme", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        // A fresh tenant is entitled to billing by default — turn it off.
        var disable = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/billing", admin,
            new { enabled = false, tier = (string?)null }));
        disable.StatusCode.ShouldBe(HttpStatusCode.OK);

        var enabled = await fixture.ScalarAsync<bool>(
            $"SELECT \"Enabled\" FROM tenant_entitlements WHERE \"TenantId\" = '{tenantId}' AND \"ModuleKey\" = 'billing'");
        enabled.ShouldBeFalse();
    }

    [Fact]
    public async Task Provisioning_creates_the_tenant_and_default_entitlements_atomically()
    {
        var admin = await AdminTokenAsync();
        var subdomain = $"atomic-{Guid.CreateVersion7():N}".Substring(0, 30);

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Atomic", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        var tenantCount = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM tenants WHERE \"Id\" = '{tenantId}' AND \"Subdomain\" = '{subdomain}'");
        tenantCount.ShouldBe(1);

        var defaultEntitlements = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM tenant_entitlements WHERE \"TenantId\" = '{tenantId}' AND \"Enabled\" = true");
        defaultEntitlements.ShouldBeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task Duplicate_subdomain_is_rejected_without_creating_a_second_tenant()
    {
        var admin = await AdminTokenAsync();
        var subdomain = $"dup-{Guid.CreateVersion7():N}".Substring(0, 30);

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "First", subdomain }));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        var duplicate = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Second", subdomain }));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var tenants = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM tenants WHERE \"Subdomain\" = '{subdomain}'");
        tenants.ShouldBe(1);
    }

    [Fact]
    public async Task A_non_admin_cannot_provision_a_tenant()
    {
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"plain-{Guid.CreateVersion7():N}@x.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", userToken,
            new { name = "Nope", subdomain = $"nope-{Guid.CreateVersion7():N}".Substring(0, 20) }));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
