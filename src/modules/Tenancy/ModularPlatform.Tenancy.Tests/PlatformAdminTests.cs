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
    public async Task Admin_can_set_module_tier_and_limits_and_detail_returns_them()
    {
        var admin = await AdminTokenAsync();
        var subdomain = $"limits-{Guid.CreateVersion7():N}".Substring(0, 30);

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Limits", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        var set = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/marketing", admin,
            new { enabled = true, tier = "pro", limits = "{ \"maxUsers\": 50, \"aiCredits\": 1000 }" }));
        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        var setData = await PlatformApiFactory.ReadData(set);
        setData.GetProperty("tier").GetString().ShouldBe("pro");
        setData.GetProperty("limits").GetString().ShouldBe("{\"maxUsers\":50,\"aiCredits\":1000}");

        var detail = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}", admin));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var marketing = (await PlatformApiFactory.ReadData(detail))
            .GetProperty("modules")
            .EnumerateArray()
            .Single(m => m.GetProperty("key").GetString() == "marketing");
        marketing.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        marketing.GetProperty("tier").GetString().ShouldBe("pro");
        using var limits = JsonDocument.Parse(marketing.GetProperty("limits").GetString()!);
        limits.RootElement.GetProperty("maxUsers").GetInt32().ShouldBe(50);
        limits.RootElement.GetProperty("aiCredits").GetInt32().ShouldBe(1000);
    }

    [Fact]
    public async Task Set_entitlement_rejects_non_object_limits_json()
    {
        var admin = await AdminTokenAsync();
        var subdomain = $"badlimits-{Guid.CreateVersion7():N}".Substring(0, 30);

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Bad Limits", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        var malformed = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/marketing", admin,
            new { enabled = true, tier = "pro", limits = "{ nope" }));
        malformed.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await malformed.Content.ReadAsStringAsync()).ShouldContain("tenant.entitlement_limits.invalid");

        var array = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put, $"/v1/tenant/admin/tenants/{tenantId}/entitlements/marketing", admin,
            new { enabled = true, tier = "pro", limits = "[1,2]" }));
        array.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await array.Content.ReadAsStringAsync()).ShouldContain("tenant.entitlement_limits.invalid");
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

    [Fact]
    public async Task Tenant_list_orders_created_at_ties_by_tenant_id_for_stable_paging()
    {
        var admin = await AdminTokenAsync();
        var tenantIds = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            var subdomain = $"stable-{i}-{Guid.CreateVersion7():N}".Substring(0, 30);
            var provision = await fixture.Client.SendAsync(fixture.Authed(
                HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = $"Stable {i}", subdomain }));
            provision.StatusCode.ShouldBe(HttpStatusCode.OK);
            tenantIds.Add((await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid());
        }

        await fixture.ExecuteSqlAsync(
            "UPDATE tenants " +
            "SET \"CreatedAt\" = timestamp with time zone '2030-01-01 00:00:00+00' " +
            $"WHERE \"Id\" IN ('{tenantIds[0]}', '{tenantIds[1]}', '{tenantIds[2]}')");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/tenant/admin/tenants?limit=3&offset=0", admin));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(response)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("tenantId").GetGuid())
            .ShouldBe(tenantIds.OrderByDescending(id => id).ToArray());
    }

    [Fact]
    public async Task Tenant_list_can_be_filtered_by_search_and_status()
    {
        var admin = await AdminTokenAsync();
        var marker = Guid.CreateVersion7().ToString("N")[..8];
        var alphaSubdomain = $"alpha-{marker}";
        var betaSubdomain = $"beta-{marker}";

        var alpha = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin,
            new { name = $"Needle Alpha {marker}", subdomain = alphaSubdomain }));
        alpha.StatusCode.ShouldBe(HttpStatusCode.OK);

        var beta = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin,
            new { name = $"Haystack Beta {marker}", subdomain = betaSubdomain }));
        beta.StatusCode.ShouldBe(HttpStatusCode.OK);

        var byName = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants?search=needle%20alpha%20{marker}&status=active", admin));
        byName.StatusCode.ShouldBe(HttpStatusCode.OK);
        var byNameData = await PlatformApiFactory.ReadData(byName);
        byNameData.GetProperty("total").GetInt32().ShouldBe(1);
        var byNameItems = byNameData.GetProperty("items").EnumerateArray().ToArray();
        byNameItems.Length.ShouldBe(1);
        byNameItems[0].GetProperty("subdomain").GetString().ShouldBe(alphaSubdomain);

        var bySubdomain = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants?search={betaSubdomain}", admin));
        bySubdomain.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bySubdomainItems = (await PlatformApiFactory.ReadData(bySubdomain)).GetProperty("items").EnumerateArray().ToArray();
        bySubdomainItems.ShouldContain(i => i.GetProperty("subdomain").GetString() == betaSubdomain);

        var invalidStatus = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants?search={marker}&status=not-a-status", admin));
        invalidStatus.StatusCode.ShouldBe(HttpStatusCode.OK);
        var invalidStatusData = await PlatformApiFactory.ReadData(invalidStatus);
        invalidStatusData.GetProperty("total").GetInt32().ShouldBe(0);
        invalidStatusData.GetProperty("items").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Admin_updates_tenant_name_and_subdomain_and_resolution_uses_the_new_subdomain()
    {
        var admin = await AdminTokenAsync();
        var (userId, userToken) = await fixture.RegisterAndLoginAsync($"tenant-edit-{Guid.CreateVersion7():N}@x.com", Password);
        var tenantId = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
        var oldSubdomain = await fixture.ScalarAsync<string>($"SELECT \"Subdomain\" FROM tenants WHERE \"Id\" = '{tenantId}'");
        var newSubdomain = $"renamed-{Guid.CreateVersion7():N}"[..30];

        var update = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}",
            admin,
            new { name = "Renamed Tenant", subdomain = newSubdomain }));
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updateData = await PlatformApiFactory.ReadData(update);
        updateData.GetProperty("name").GetString().ShouldBe("Renamed Tenant");
        updateData.GetProperty("subdomain").GetString().ShouldBe(newSubdomain);

        var detail = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}", admin));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detailData = await PlatformApiFactory.ReadData(detail);
        detailData.GetProperty("name").GetString().ShouldBe("Renamed Tenant");
        detailData.GetProperty("subdomain").GetString().ShouldBe(newSubdomain);

        var oldHost = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", userToken);
        oldHost.Headers.Host = $"{oldSubdomain}.lvh.me";
        (await fixture.Client.SendAsync(oldHost)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var newHost = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", userToken);
        newHost.Headers.Host = $"{newSubdomain}.lvh.me";
        (await fixture.Client.SendAsync(newHost)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var auditRows = await fixture.ScalarAsync<long>(
            "SELECT count(*)::bigint FROM tenancy_audit_entries " +
            "WHERE \"EntityType\" = 'Tenant' AND \"Action\" = 'Update' " +
            $"AND \"EntityId\" = '{tenantId}'");
        auditRows.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Update_tenant_rejects_duplicate_reserved_or_invalid_subdomains_without_changing_the_tenant()
    {
        var admin = await AdminTokenAsync();
        var firstSubdomain = $"upd-a-{Guid.CreateVersion7():N}"[..30];
        var secondSubdomain = $"upd-b-{Guid.CreateVersion7():N}"[..30];

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "First", subdomain = firstSubdomain }));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstTenantId = (await PlatformApiFactory.ReadData(first)).GetProperty("tenantId").GetGuid();

        var second = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Second", subdomain = secondSubdomain }));
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        var duplicate = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{firstTenantId}",
            admin,
            new { name = "Collision", subdomain = secondSubdomain }));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadAsStringAsync()).ShouldContain("tenant.subdomain_taken");

        var reserved = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{firstTenantId}",
            admin,
            new { name = "Reserved", subdomain = "admin" }));
        reserved.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await reserved.Content.ReadAsStringAsync()).ShouldContain("tenant.subdomain.reserved");

        var invalid = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{firstTenantId}",
            admin,
            new { name = "Invalid", subdomain = "-bad" }));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).ShouldContain("tenant.subdomain.invalid");

        var detail = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{firstTenantId}", admin));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        var detailData = await PlatformApiFactory.ReadData(detail);
        detailData.GetProperty("name").GetString().ShouldBe("First");
        detailData.GetProperty("subdomain").GetString().ShouldBe(firstSubdomain);
    }

    [Fact]
    public async Task A_non_admin_cannot_update_a_tenant()
    {
        var admin = await AdminTokenAsync();
        var (_, userToken) = await fixture.RegisterAndLoginAsync($"plain-update-{Guid.CreateVersion7():N}@x.com", Password);
        var subdomain = $"noedit-{Guid.CreateVersion7():N}"[..30];

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "No edit", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}",
            userToken,
            new { name = "Nope", subdomain = $"nope-{Guid.CreateVersion7():N}"[..30] }));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
