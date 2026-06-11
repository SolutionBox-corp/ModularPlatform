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
    public async Task A_non_admin_cannot_provision_a_tenant()
    {
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"plain-{Guid.CreateVersion7():N}@x.com", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", userToken,
            new { name = "Nope", subdomain = $"nope-{Guid.CreateVersion7():N}".Substring(0, 20) }));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
