using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

        var entitlementId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM tenant_entitlements WHERE \"TenantId\" = '{tenantId}' AND \"ModuleKey\" = 'billing'");
        var auditRows = await fixture.ScalarAsync<long>(
            "SELECT count(*)::bigint FROM tenancy_audit_entries " +
            "WHERE \"EntityType\" = 'TenantEntitlement' AND \"Action\" = 'Update' " +
            $"AND \"EntityId\" = '{entitlementId}'");
        auditRows.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Set_entitlement_rejects_unknown_module_keys_without_persisting_the_typo()
    {
        var admin = await AdminTokenAsync();
        var subdomain = $"typo-{Guid.CreateVersion7():N}".Substring(0, 30);

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Typo", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        var typo = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/crmm", admin,
            new { enabled = true, tier = (string?)null }));
        typo.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var persistedTypos = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM tenant_entitlements WHERE \"TenantId\" = '{tenantId}' AND \"ModuleKey\" = 'crmm'");
        persistedTypos.ShouldBe(0);
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

    [Fact]
    public async Task Tenant_list_is_platform_admin_only_paged_and_contains_no_internal_registry_fields()
    {
        var admin = await AdminTokenAsync();
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"plain-list-{Guid.CreateVersion7():N}@x.com", Password);

        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/admin/tenants", userToken));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        for (var i = 0; i < 3; i++)
        {
            var subdomain = $"list-{i}-{Guid.CreateVersion7():N}".Substring(0, 30);
            var provision = await fixture.Client.SendAsync(fixture.Authed(
                HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = $"List {i}", subdomain }));
            provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var firstPage = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/admin/tenants?limit=2&offset=0", admin));
        firstPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstData = await PlatformApiFactory.ReadData(firstPage);

        firstData.GetProperty("limit").GetInt32().ShouldBe(2);
        firstData.GetProperty("offset").GetInt32().ShouldBe(0);
        firstData.GetProperty("total").GetInt32().ShouldBeGreaterThanOrEqualTo(4);

        var firstItems = firstData.GetProperty("items").EnumerateArray().ToArray();
        firstItems.Length.ShouldBe(2);
        AssertPublicTenantListShape(firstItems[0]);

        var secondPage = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/admin/tenants?limit=2&offset=2", admin));
        secondPage.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondItems = (await PlatformApiFactory.ReadData(secondPage)).GetProperty("items").EnumerateArray().ToArray();
        secondItems.Length.ShouldBe(2);

        var firstIds = firstItems.Select(i => i.GetProperty("tenantId").GetGuid()).ToHashSet();
        var secondIds = secondItems.Select(i => i.GetProperty("tenantId").GetGuid()).ToHashSet();
        firstIds.Intersect(secondIds).ShouldBeEmpty();
    }

    private static void AssertPublicTenantListShape(JsonElement item)
    {
        var names = item.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        names.ShouldContain("tenantId");
        names.ShouldContain("subdomain");
        names.ShouldContain("name");
        names.ShouldContain("status");
        names.ShouldContain("placement");
        names.ShouldContain("createdAt");

        names.ShouldNotContain("dbDsnSecretRef");
        names.ShouldNotContain("infraRevision");
        names.ShouldNotContain("modules");
        names.ShouldNotContain("entitlements");
    }

    [Fact]
    public async Task Tenant_detail_is_platform_admin_only_returns_404_for_missing_tenant_and_shows_persisted_entitlements()
    {
        var admin = await AdminTokenAsync();
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"plain-detail-{Guid.CreateVersion7():N}@x.com", Password);
        var subdomain = $"detail-{Guid.CreateVersion7():N}".Substring(0, 30);

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Detail", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}", userToken));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var missing = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{Guid.CreateVersion7()}", admin));
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var detail = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}", admin));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(detail);
        data.GetProperty("tenantId").GetGuid().ShouldBe(tenantId);
        data.GetProperty("subdomain").GetString().ShouldBe(subdomain);
        ModuleEnabled(data, "billing").ShouldBeTrue();

        var disable = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/billing", admin,
            new { enabled = false, tier = (string?)null }));
        disable.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterToggle = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}", admin));
        afterToggle.StatusCode.ShouldBe(HttpStatusCode.OK);
        ModuleEnabled(await PlatformApiFactory.ReadData(afterToggle), "billing").ShouldBeFalse();
    }

    private static bool ModuleEnabled(JsonElement tenantDetail, string moduleKey) =>
        tenantDetail.GetProperty("modules").EnumerateArray()
            .Any(m => string.Equals(m.GetProperty("key").GetString(), moduleKey, StringComparison.Ordinal)
                && m.GetProperty("enabled").GetBoolean());
}
