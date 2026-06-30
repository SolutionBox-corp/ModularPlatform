using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// The tenant-resolution middleware maps the request host's subdomain to a tenant and cross-checks it against the
/// token: the right subdomain passes, an unknown one 404s, and a token used on ANOTHER tenant's subdomain is 401
/// (defence-in-depth IDOR guard). Requests on the default (apex/localhost) host are unaffected — the middleware no-ops.
/// </summary>
[Collection("Integration")]
public sealed class TenantResolutionTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    private async Task<(Guid TenantId, string Access, string Subdomain)> RegisterAndSubdomainAsync()
    {
        var (userId, access) = await fixture.RegisterAndLoginAsync($"sub-{Guid.CreateVersion7():N}@x.com", Password);
        var tenantId = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
        var subdomain = await fixture.ScalarAsync<string>($"SELECT \"Subdomain\" FROM tenants WHERE \"Id\" = '{tenantId}'");
        return (tenantId, access, subdomain);
    }

    [Fact]
    public async Task A_token_on_its_own_subdomain_passes()
    {
        var (_, access, subdomain) = await RegisterAndSubdomainAsync();

        var request = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access);
        request.Headers.Host = $"{subdomain}.lvh.me";
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_subdomain_is_404()
    {
        var (_, access, _) = await RegisterAndSubdomainAsync();

        var request = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access);
        request.Headers.Host = $"nope-{Guid.CreateVersion7():N}.lvh.me";
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_token_on_another_tenants_subdomain_is_401()
    {
        var (_, accessA, _) = await RegisterAndSubdomainAsync();
        var (_, _, subdomainB) = await RegisterAndSubdomainAsync();

        var request = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", accessA);
        request.Headers.Host = $"{subdomainB}.lvh.me";
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Suspended_tenant_subdomain_is_404_until_reactivated()
    {
        var (tenantId, access, subdomain) = await RegisterAndSubdomainAsync();
        var admin = await AdminTokenAsync();

        var suspend = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}/status",
            admin,
            new { status = "suspended" }));
        suspend.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(suspend)).GetProperty("status").GetString().ShouldBe("Suspended");

        var blocked = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access);
        blocked.Headers.Host = $"{subdomain}.lvh.me";
        (await fixture.Client.SendAsync(blocked)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var reactivate = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}/status",
            admin,
            new { status = "Active" }));
        reactivate.StatusCode.ShouldBe(HttpStatusCode.OK);

        var allowed = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access);
        allowed.Headers.Host = $"{subdomain}.lvh.me";
        (await fixture.Client.SendAsync(allowed)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Set_tenant_status_rejects_internal_or_unknown_states()
    {
        var (tenantId, _, _) = await RegisterAndSubdomainAsync();
        var admin = await AdminTokenAsync();

        var separating = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}/status",
            admin,
            new { status = "Separating" }));
        separating.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await separating.Content.ReadAsStringAsync()).ShouldContain("tenant.status.invalid");

        var nonsense = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}/status",
            admin,
            new { status = "gone" }));
        nonsense.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await nonsense.Content.ReadAsStringAsync()).ShouldContain("tenant.status.invalid");
    }

    private async Task<string> AdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });

        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.EnsureSuccessStatusCode();

        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
