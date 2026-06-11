using System.Net;
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
    private async Task<(string Access, string Subdomain)> RegisterAndSubdomainAsync()
    {
        var (userId, access) = await fixture.RegisterAndLoginAsync($"sub-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var tenantId = await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
        var subdomain = await fixture.ScalarAsync<string>($"SELECT \"Subdomain\" FROM tenants WHERE \"Id\" = '{tenantId}'");
        return (access, subdomain);
    }

    [Fact]
    public async Task A_token_on_its_own_subdomain_passes()
    {
        var (access, subdomain) = await RegisterAndSubdomainAsync();

        var request = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access);
        request.Headers.Host = $"{subdomain}.lvh.me";
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_subdomain_is_404()
    {
        var (access, _) = await RegisterAndSubdomainAsync();

        var request = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", access);
        request.Headers.Host = $"nope-{Guid.CreateVersion7():N}.lvh.me";
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_token_on_another_tenants_subdomain_is_401()
    {
        var (accessA, _) = await RegisterAndSubdomainAsync();
        var (_, subdomainB) = await RegisterAndSubdomainAsync();

        var request = fixture.Authed(HttpMethod.Get, "/v1/tenant/me/entitlements", accessA);
        request.Headers.Host = $"{subdomainB}.lvh.me";
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
