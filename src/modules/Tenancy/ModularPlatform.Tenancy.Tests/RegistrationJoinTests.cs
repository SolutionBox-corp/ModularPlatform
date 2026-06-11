using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// B2B registration: a user signing up ON a tenant's subdomain JOINS that existing tenant (it is the server-resolved
/// tenant, never the body). A signup with no subdomain (apex/localhost) still self-serve-provisions a fresh tenant.
/// This is what lets multiple users share one tenant — the prerequisite for per-tenant catalogue + commerce.
/// </summary>
[Collection("Integration")]
public sealed class RegistrationJoinTests(PlatformApiFactory fixture)
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
    public async Task Registering_on_a_tenant_subdomain_joins_that_tenant()
    {
        var admin = await AdminTokenAsync();
        var subdomain = $"joingym{Guid.CreateVersion7():N}".Substring(0, 24);

        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Join Gym", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tenantId = (await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid();

        // Two members sign up on the gym's subdomain — both must land in the SAME (provisioned) tenant.
        var u1 = await RegisterOnHostAsync($"m1-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me");
        var u2 = await RegisterOnHostAsync($"m2-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me");

        (await TenantOfAsync(u1)).ShouldBe(tenantId);
        (await TenantOfAsync(u2)).ShouldBe(tenantId);
    }

    [Fact]
    public async Task Registering_without_a_subdomain_still_provisions_a_fresh_tenant()
    {
        var u1 = await RegisterOnHostAsync($"apex1-{Guid.CreateVersion7():N}@x.com", host: null);
        var u2 = await RegisterOnHostAsync($"apex2-{Guid.CreateVersion7():N}@x.com", host: null);

        (await TenantOfAsync(u1)).ShouldNotBe(await TenantOfAsync(u2));
    }

    private async Task<Guid> RegisterOnHostAsync(string email, string? host)
    {
        var request = fixture.Authed(HttpMethod.Post, "/v1/identity/users", accessToken: "", body: null);
        request.Headers.Authorization = null; // anonymous signup
        request.Content = JsonContent.Create(new { email, password = Password });
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        var response = await fixture.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await PlatformApiFactory.ReadData(response)).GetProperty("userId").GetGuid();
    }

    private async Task<Guid> TenantOfAsync(Guid userId) =>
        await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
}
