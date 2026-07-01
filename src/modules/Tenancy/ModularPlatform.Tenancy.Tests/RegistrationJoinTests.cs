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
    public async Task Admin_can_switch_registration_mode_between_open_and_closed()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(admin);

        var open = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}/registration-mode",
            admin,
            new { registrationMode = "open" }));
        open.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(open)).GetProperty("registrationMode").GetString().ShouldBe("Open");

        var detail = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}", admin));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(detail)).GetProperty("registrationMode").GetString().ShouldBe("Open");

        var joinedWithoutInvite = await RegisterOnHostAsync(
            $"open-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", inviteToken: null);
        joinedWithoutInvite.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await TenantOfAsync((await PlatformApiFactory.ReadData(joinedWithoutInvite)).GetProperty("userId").GetGuid()))
            .ShouldBe(tenantId);

        var closed = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}/registration-mode",
            admin,
            new { registrationMode = "Closed" }));
        closed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(closed)).GetProperty("registrationMode").GetString().ShouldBe("Closed");

        var blocked = await RegisterOnHostAsync(
            $"closed-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", inviteToken: null);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Set_registration_mode_rejects_unknown_values()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, _) = await ProvisionTenantAsync(admin);

        var invalid = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Put,
            $"/v1/tenant/admin/tenants/{tenantId}/registration-mode",
            admin,
            new { registrationMode = "public-ish" }));

        invalid.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await invalid.Content.ReadAsStringAsync()).ShouldContain("tenant.registration_mode.invalid");
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
    public async Task Admin_can_list_invites_without_exposing_raw_token_or_hash()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, _) = await ProvisionTenantAsync(admin);
        var token = await CreateInviteAsync(admin, tenantId);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}/invites", admin));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(token);
        body.ShouldNotContain("inviteToken");
        body.ShouldNotContain("tokenHash");

        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("total").GetInt32().ShouldBe(1);
        var item = data.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("status").GetString().ShouldBe("Pending");
        item.GetProperty("inviteId").GetGuid().ShouldNotBe(Guid.Empty);
        item.GetProperty("expiresAt").GetDateTimeOffset().ShouldBeGreaterThan(DateTimeOffset.UtcNow);

        var invalidStatus = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}/invites?status=maybe", admin));
        invalidStatus.StatusCode.ShouldBe(HttpStatusCode.OK);
        var invalidStatusData = await PlatformApiFactory.ReadData(invalidStatus);
        invalidStatusData.GetProperty("total").GetInt32().ShouldBe(0);
        invalidStatusData.GetProperty("items").EnumerateArray().ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoked_invite_cannot_be_used_and_revoke_is_idempotent()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(admin);
        var token = await CreateInviteAsync(admin, tenantId);
        var inviteId = await FirstInviteIdAsync(admin, tenantId, "Pending");

        var revoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Delete, $"/v1/tenant/admin/tenants/{tenantId}/invites/{inviteId}", admin));
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        var revokeData = await PlatformApiFactory.ReadData(revoke);
        revokeData.GetProperty("status").GetString().ShouldBe("Revoked");
        revokeData.GetProperty("revokedAt").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);

        var secondRevoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Delete, $"/v1/tenant/admin/tenants/{tenantId}/invites/{inviteId}", admin));
        secondRevoke.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(secondRevoke)).GetProperty("status").GetString().ShouldBe("Revoked");

        var blocked = await RegisterOnHostAsync(
            $"revoked-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", token);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var revokedList = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}/invites?status=revoked", admin));
        revokedList.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(revokedList)).GetProperty("items").EnumerateArray()
            .ShouldContain(i => i.GetProperty("inviteId").GetGuid() == inviteId);
    }

    [Fact]
    public async Task Invite_list_reports_consumed_and_expired_statuses()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(admin);
        var consumedToken = await CreateInviteAsync(admin, tenantId);

        var joined = await RegisterOnHostAsync(
            $"consumed-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", consumedToken);
        joined.StatusCode.ShouldBe(HttpStatusCode.Created);

        _ = await CreateInviteAsync(admin, tenantId);
        await fixture.ExecuteSqlAsync(
            $"UPDATE tenant_invites SET \"ExpiresAt\" = now() - interval '1 minute' " +
            $"WHERE \"TenantId\" = '{tenantId}' AND \"ConsumedAt\" IS NULL AND \"RevokedAt\" IS NULL");

        var consumed = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}/invites?status=consumed", admin));
        consumed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var consumedItems = (await PlatformApiFactory.ReadData(consumed)).GetProperty("items").EnumerateArray().ToArray();
        consumedItems.ShouldContain(i => i.GetProperty("status").GetString() == "Consumed");
        consumedItems.ShouldAllBe(i => i.GetProperty("consumedAt").ValueKind != System.Text.Json.JsonValueKind.Null);

        var expired = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}/invites?status=expired", admin));
        expired.StatusCode.ShouldBe(HttpStatusCode.OK);
        var expiredItems = (await PlatformApiFactory.ReadData(expired)).GetProperty("items").EnumerateArray().ToArray();
        expiredItems.ShouldContain(i => i.GetProperty("status").GetString() == "Expired");
        expiredItems.ShouldAllBe(i => i.GetProperty("consumedAt").ValueKind == System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task An_expired_invite_cannot_be_used_to_join_the_tenant()
    {
        var admin = await AdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(admin);
        var token = await CreateInviteAsync(admin, tenantId);

        await fixture.ExecuteSqlAsync(
            $"UPDATE tenant_invites SET \"ExpiresAt\" = now() - interval '1 minute' WHERE \"TenantId\" = '{tenantId}'");

        var response = await RegisterOnHostAsync(
            $"expired-{Guid.CreateVersion7():N}@x.com", $"{subdomain}.lvh.me", token);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_invite_for_one_tenant_cannot_join_another_tenant()
    {
        var admin = await AdminTokenAsync();
        var (tenantA, subdomainA) = await ProvisionTenantAsync(admin);
        var (_, subdomainB) = await ProvisionTenantAsync(admin);
        var tokenForA = await CreateInviteAsync(admin, tenantA);

        var wrongTenant = await RegisterOnHostAsync(
            $"wrong-{Guid.CreateVersion7():N}@x.com", $"{subdomainB}.lvh.me", tokenForA);
        wrongTenant.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var rightTenant = await RegisterOnHostAsync(
            $"right-{Guid.CreateVersion7():N}@x.com", $"{subdomainA}.lvh.me", tokenForA);
        rightTenant.StatusCode.ShouldBe(HttpStatusCode.Created);
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

    private async Task<Guid> FirstInviteIdAsync(string admin, Guid tenantId, string status)
    {
        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/tenant/admin/tenants/{tenantId}/invites?status={status}", admin));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await PlatformApiFactory.ReadData(response))
            .GetProperty("items")
            .EnumerateArray()
            .First()
            .GetProperty("inviteId")
            .GetGuid();
    }

    private async Task<Guid> TenantOfAsync(Guid userId) =>
        await fixture.ScalarAsync<Guid>($"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{userId}'");
}
