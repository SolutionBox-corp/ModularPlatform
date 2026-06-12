using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Tenancy.Tests;

/// <summary>
/// B2B registration gating: a tenant is <c>InviteOnly</c> by default, so signing up ON its subdomain REQUIRES a
/// valid single-use invite — an open join is rejected. A signup with no subdomain (apex/localhost) still self-serve
/// provisions a fresh tenant (the creator is the first member; no invite needed). Invites are single-use.
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

    private async Task<(Guid TenantId, string Subdomain)> ProvisionTenantAsync(string admin)
    {
        var subdomain = $"gym{Guid.CreateVersion7():N}".Substring(0, 20);
        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/tenant/admin/tenants", admin, new { name = "Gym", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);
        return ((await PlatformApiFactory.ReadData(provision)).GetProperty("tenantId").GetGuid(), subdomain);
    }

    private async Task<string> CreateInviteAsync(string admin, Guid tenantId)
    {
        var invite = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/tenant/admin/tenants/{tenantId}/invites", admin, new { expiresInDays = 7 }));
        invite.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(invite)).GetProperty("inviteToken").GetString()!;
    }

    [Fact]
    public async Task Registering_on_a_subdomain_without_an_invite_is_rejected()
    {
        var admin = await AdminTokenAsync();
        var (_, subdomain) = await ProvisionTenantAsync(admin);

        var response = await RegisterOnHostAsync(
            $"noinv-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", inviteToken: null);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Registering_on_a_subdomain_with_a_valid_invite_joins_the_tenant()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(admin);
        var token = await CreateInviteAsync(admin, tenantId);

        var response = await RegisterOnHostAsync(
            $"inv-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", inviteToken: token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var userId = (await PlatformApiFactory.ReadData(response)).GetProperty("userId").GetGuid();
        (await TenantOfAsync(userId)).ShouldBe(tenantId);
    }

    [Fact]
    public async Task An_invite_is_single_use()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(admin);
        var token = await CreateInviteAsync(admin, tenantId);

        var first = await RegisterOnHostAsync($"u1-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", token);
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        // The same token cannot be reused by a second signup.
        var second = await RegisterOnHostAsync($"u2-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", token);
        second.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Registering_without_a_subdomain_still_provisions_a_fresh_tenant()
    {
        var u1 = await RegisterOnHostAsync($"apex1-{Guid.CreateVersion7():N}@x.com", host: null, inviteToken: null);
        var u2 = await RegisterOnHostAsync($"apex2-{Guid.CreateVersion7():N}@x.com", host: null, inviteToken: null);
        u1.StatusCode.ShouldBe(HttpStatusCode.Created);
        u2.StatusCode.ShouldBe(HttpStatusCode.Created);

        var t1 = await TenantOfAsync((await PlatformApiFactory.ReadData(u1)).GetProperty("userId").GetGuid());
        var t2 = await TenantOfAsync((await PlatformApiFactory.ReadData(u2)).GetProperty("userId").GetGuid());
        t1.ShouldNotBe(t2);
    }

    private async Task<HttpResponseMessage> RegisterOnHostAsync(string email, string? host, string? inviteToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/identity/users")
        {
            Content = JsonContent.Create(new { email, password = Password, inviteToken }),
        };
        if (host is not null)
        {
            request.Headers.Host = host;
        }

        return await fixture.Client.SendAsync(request);
    }

    private async Task<Guid> TenantOfAsync(Guid userId) =>
        await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
}
