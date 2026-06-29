using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// <c>GET /v1/tenant/me/entitlements</c> is the single source for the FE nav. A freshly provisioned tenant is
/// entitled to the default product modules; the response carries them keyed + enabled. Tenant comes from the token.
/// </summary>
[Collection("Integration")]
public sealed class EntitlementsTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task A_fresh_tenant_is_entitled_to_the_default_product_modules()
    {
        var (_, access) = await fixture.RegisterAndLoginAsync($"ent-{Guid.CreateVersion7():N}@x.com", Password);

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        var enabled = data.GetProperty("modules").EnumerateArray()
            .Where(m => m.GetProperty("enabled").GetBoolean())
            .Select(m => m.GetProperty("key").GetString())
            .ToHashSet();

        enabled.ShouldContain("billing");
        enabled.ShouldContain("notifications");
        enabled.ShouldContain("files");
        enabled.ShouldContain("operations");
        enabled.ShouldContain("gdpr");
        enabled.ShouldContain("crm");
    }

    [Fact]
    public async Task The_entitlements_endpoint_requires_authentication()
    {
        var response = await fixture.Client.GetAsync("/v1/tenant/me/entitlements");
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_toggle_is_visible_on_next_entitlements_read()
    {
        var admin = await AdminTokenAsync();
        var (userId, access) = await fixture.RegisterAndLoginAsync($"ent-toggle-{Guid.CreateVersion7():N}@x.com", Password);
        var tenantId = await TenantIdOf(userId);

        (await ModuleEnabledAsync(access, "billing")).ShouldBeTrue();

        var disable = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/billing", admin,
            new { enabled = false, tier = (string?)null }));
        disable.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await ModuleEnabledAsync(access, "billing")).ShouldBeFalse();
    }

    [Fact]
    public async Task Backend_guard_blocks_a_disabled_module_even_if_the_nav_is_bypassed()
    {
        var admin = await AdminTokenAsync();
        var (userId, access) = await fixture.RegisterAndLoginAsync($"ent-guard-{Guid.CreateVersion7():N}@x.com", Password);
        var tenantId = await TenantIdOf(userId);

        var allowed = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/billing/packages", access));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var disable = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/billing", admin,
            new { enabled = false, tier = (string?)null }));
        disable.StatusCode.ShouldBe(HttpStatusCode.OK);

        var bypass = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/billing/packages", access));
        bypass.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private async Task<Guid> TenantIdOf(Guid userId) =>
        await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");

    private async Task<bool> ModuleEnabledAsync(string accessToken, string moduleKey)
    {
        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", accessToken));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(response);
        return data.GetProperty("modules").EnumerateArray()
            .Any(m => string.Equals(m.GetProperty("key").GetString(), moduleKey, StringComparison.Ordinal)
                && m.GetProperty("enabled").GetBoolean());
    }

    private async Task<string> AdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue();
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
