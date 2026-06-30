using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Authorization model end-to-end: a permission-gated admin endpoint rejects a normal user (403), the configured
/// admin email is bootstrapped to the admin role on login and its token carries the permission claim, the admin
/// can grant a role to another user, and that user's next token carries the new permission (claims are a snapshot
/// refreshed on re-auth).
/// </summary>
[Collection("Integration")]
public sealed class AuthzTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Permission_gated_endpoint_rejects_non_admins_and_admins_can_grant_roles()
    {
        var normalEmail = $"user-{Guid.CreateVersion7():N}@x.com";
        var (normalId, normalToken) = await fixture.RegisterAndLoginAsync(normalEmail, Password);

        // A normal authenticated user is FORBIDDEN from the permission-gated admin endpoint.
        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{normalId}/roles", normalToken, new { role = "admin" }));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // The configured admin email gets the admin role on login; its token carries the manage-roles permission.
        var adminToken = await EnsureAdminTokenAsync();
        ClaimValues(adminToken, "permission").ShouldContain("identity.manage_roles");
        ClaimValues(adminToken, "role").ShouldContain("admin");

        // The admin grants the normal user the admin role.
        var granted = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{normalId}/roles", adminToken, new { role = "admin" }));
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);

        // After re-authenticating, the normal user's NEW token carries the permission and the endpoint now allows.
        var reloginToken = await LoginAsync(normalEmail, Password);
        ClaimValues(reloginToken, "permission").ShouldContain("identity.manage_roles");

        var nowAllowed = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{normalId}/roles", reloginToken, new { role = "admin" }));
        nowAllowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Refreshed_token_carries_role_changes_while_the_old_access_token_stays_a_snapshot()
    {
        var normalEmail = $"refresh-claims-{Guid.CreateVersion7():N}@x.com";
        var (normalId, accessToken, refreshToken) = await RegisterLoginWithTokensAsync(normalEmail);
        var adminToken = await EnsureAdminTokenAsync();

        ClaimValues(accessToken, "permission").ShouldNotContain("identity.manage_roles");

        var granted = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{normalId}/roles", adminToken, new { role = "admin" }));
        granted.StatusCode.ShouldBe(HttpStatusCode.OK);

        var oldSnapshotStillForbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{normalId}/roles", accessToken, new { role = "admin" }));
        oldSnapshotStillForbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken });
        refresh.EnsureSuccessStatusCode();
        var refreshedAccessToken = (await PlatformApiFactory.ReadData(refresh)).GetProperty("accessToken").GetString()!;

        ClaimValues(refreshedAccessToken, "permission").ShouldContain("identity.manage_roles");
        var refreshedTokenAllows = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{normalId}/roles", refreshedAccessToken, new { role = "admin" }));
        refreshedTokenAllows.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Concurrent_identical_role_grants_are_idempotent_not_a_500()
    {
        var email = $"user-{Guid.CreateVersion7():N}@x.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var adminToken = await EnsureAdminTokenAsync();

        // Two identical grants in flight at once: the read-then-insert pre-check cannot prevent the race, so the
        // handler must absorb the UNIQUE(UserId, RoleId) violation as an idempotent success, never a 500.
        var grants = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
            fixture.Client.SendAsync(fixture.Authed(
                HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", adminToken, new { role = "admin" }))));

        grants.ShouldAllBe(r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task Assign_role_returns_not_found_for_unknown_user_or_role()
    {
        var adminToken = await EnsureAdminTokenAsync();

        var unknownUser = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/identity/admin/users/{Guid.CreateVersion7()}/roles",
            adminToken,
            new { role = "admin" }));
        unknownUser.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknownUser.Content.ReadAsStringAsync()).ShouldContain("user.not_found");

        var (userId, _) = await fixture.RegisterAndLoginAsync(
            $"assign-missing-role-{Guid.CreateVersion7():N}@x.com", Password);
        var unknownRole = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/identity/admin/users/{userId}/roles",
            adminToken,
            new { role = "missing-role" }));
        unknownRole.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknownRole.Content.ReadAsStringAsync()).ShouldContain("role.not_found");
    }

    [Fact]
    public async Task Revoke_role_is_idempotent_and_removes_permission_only_from_new_tokens()
    {
        var email = $"revoke-role-{Guid.CreateVersion7():N}@x.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var adminToken = await EnsureAdminTokenAsync();

        var missingAssignment = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Delete, $"/v1/identity/admin/users/{userId}/roles/admin", adminToken));
        missingAssignment.StatusCode.ShouldBe(HttpStatusCode.OK);

        var grant = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", adminToken, new { role = "admin" }));
        grant.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tokenWithRole = await LoginWithTokenExpiryAsync(email, Password);
        tokenWithRole.AccessTokenExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        tokenWithRole.AccessTokenExpiresAt.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(11));
        ClaimValues(tokenWithRole.AccessToken, "permission").ShouldContain("identity.manage_roles");

        var revoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Delete, $"/v1/identity/admin/users/{userId}/roles/admin", adminToken));
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        var staleTokenStillAllowed = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", tokenWithRole.AccessToken, new { role = "admin" }));
        staleTokenStillAllowed.StatusCode.ShouldBe(HttpStatusCode.OK);

        var revokeAgain = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Delete, $"/v1/identity/admin/users/{userId}/roles/admin", adminToken));
        revokeAgain.StatusCode.ShouldBe(HttpStatusCode.OK);

        var freshTokenAfterRevoke = await LoginAsync(email, Password);
        ClaimValues(freshTokenAfterRevoke, "permission").ShouldNotContain("identity.manage_roles");

        var freshTokenForbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", freshTokenAfterRevoke, new { role = "admin" }));
        freshTokenForbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Revoke_role_is_a_tracked_delete_that_writes_audit()
    {
        var email = $"revoke-audit-{Guid.CreateVersion7():N}@x.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var adminToken = await EnsureAdminTokenAsync();

        var grant = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", adminToken, new { role = "admin" }));
        grant.StatusCode.ShouldBe(HttpStatusCode.OK);

        var assignmentId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"Id\" FROM user_roles WHERE \"UserId\" = '{userId}'");

        var revoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Delete, $"/v1/identity/admin/users/{userId}/roles/admin", adminToken));
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        await fixture.WaitForCountAsync(
            "SELECT count(*)::bigint FROM identity_audit_entries " +
            "WHERE \"EntityType\" = 'UserRole' AND \"Action\" = 'Delete' " +
            $"AND \"EntityId\" = '{assignmentId}'",
            1);
    }

    [Fact]
    public async Task Get_user_detail_requires_permission_and_returns_projected_current_roles()
    {
        var email = $"detail-{Guid.CreateVersion7():N}@x.com";
        var (userId, normalToken) = await fixture.RegisterAndLoginAsync(email, Password);

        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/identity/admin/users/{userId}", normalToken));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var adminToken = await EnsureAdminTokenAsync();
        var grant = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", adminToken, new { role = "admin" }));
        grant.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tenantAdminToken = await LoginAsync(email, Password);
        var detail = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/identity/admin/users/{userId}", tenantAdminToken));
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(detail);
        data.GetProperty("id").GetGuid().ShouldBe(userId);
        data.GetProperty("email").GetString().ShouldBe(email);
        data.GetProperty("isLocked").GetBoolean().ShouldBeFalse();
        data.GetProperty("createdAt").GetDateTimeOffset().ShouldNotBe(default);
        data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ShouldContain("admin");
    }

    [Fact]
    public async Task Get_user_detail_is_tenant_scoped_and_hides_soft_deleted_users()
    {
        var tenantAdminEmail = $"tenant-admin-{Guid.CreateVersion7():N}@x.com";
        var otherTenantEmail = $"other-tenant-{Guid.CreateVersion7():N}@x.com";
        var deletedEmail = $"deleted-detail-{Guid.CreateVersion7():N}@x.com";
        var (tenantAdminId, _) = await fixture.RegisterAndLoginAsync(tenantAdminEmail, Password);
        var (otherTenantUserId, _) = await fixture.RegisterAndLoginAsync(otherTenantEmail, Password);
        var (deletedUserId, _) = await fixture.RegisterAndLoginAsync(deletedEmail, Password);
        var platformAdminToken = await EnsureAdminTokenAsync();

        foreach (var userId in new[] { tenantAdminId, deletedUserId })
        {
            var grant = await fixture.Client.SendAsync(fixture.Authed(
                HttpMethod.Post, $"/v1/identity/admin/users/{userId}/roles", platformAdminToken, new { role = "admin" }));
            grant.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var tenantAdminToken = await LoginAsync(tenantAdminEmail, Password);
        var crossTenant = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/identity/admin/users/{otherTenantUserId}", tenantAdminToken));
        crossTenant.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var deletedUserToken = await LoginAsync(deletedEmail, Password);
        await fixture.ExecuteSqlAsync(
            $"UPDATE users SET \"DeletedAt\" = now() WHERE \"Id\" = '{deletedUserId}'");

        var deleted = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/identity/admin/users/{deletedUserId}", deletedUserToken));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_user_detail_allows_a_tenant_admin_to_read_another_user_in_the_same_tenant()
    {
        var platformAdminToken = await EnsureAdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(platformAdminToken);
        var tenantAdminEmail = $"same-tenant-admin-{Guid.CreateVersion7():N}@x.com";
        var memberEmail = $"same-tenant-member-{Guid.CreateVersion7():N}@x.com";

        var tenantAdminId = await RegisterOnTenantHostAsync(
            tenantAdminEmail, $"{subdomain}.lvh.me", await CreateInviteAsync(platformAdminToken, tenantId));
        var memberId = await RegisterOnTenantHostAsync(
            memberEmail, $"{subdomain}.lvh.me", await CreateInviteAsync(platformAdminToken, tenantId));

        var tenantAdminTenantId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{tenantAdminId}'");
        var memberTenantId = await fixture.ScalarAsync<Guid>(
            $"SELECT \"TenantId\" FROM users WHERE \"Id\" = '{memberId}'");
        tenantAdminTenantId.ShouldBe(tenantId);
        memberTenantId.ShouldBe(tenantId);

        var grant = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{tenantAdminId}/roles", platformAdminToken, new { role = "admin" }));
        grant.StatusCode.ShouldBe(HttpStatusCode.OK);

        var tenantAdminToken = await LoginAsync(tenantAdminEmail, Password);
        var sameTenantDetail = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, $"/v1/identity/admin/users/{memberId}", tenantAdminToken));
        sameTenantDetail.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(sameTenantDetail);
        data.GetProperty("id").GetGuid().ShouldBe(memberId);
        data.GetProperty("email").GetString().ShouldBe(memberEmail);
    }

    [Fact]
    public async Task Tenant_user_picker_lists_only_same_tenant_users_for_assignees()
    {
        var platformAdminToken = await EnsureAdminTokenAsync();
        var (tenantId, subdomain) = await ProvisionTenantAsync(platformAdminToken);
        var userEmail = $"tenant-picker-user-{Guid.CreateVersion7():N}@x.com";
        var memberEmail = $"tenant-picker-member-{Guid.CreateVersion7():N}@x.com";
        var otherEmail = $"tenant-picker-other-{Guid.CreateVersion7():N}@x.com";

        var userId = await RegisterOnTenantHostAsync(
            userEmail, $"{subdomain}.lvh.me", await CreateInviteAsync(platformAdminToken, tenantId));
        var memberId = await RegisterOnTenantHostAsync(
            memberEmail, $"{subdomain}.lvh.me", await CreateInviteAsync(platformAdminToken, tenantId));
        var (otherId, _) = await fixture.RegisterAndLoginAsync(otherEmail, Password);
        var userToken = await LoginAsync(userEmail, Password);

        var list = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/identity/users?page=1&pageSize=20", userToken));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await PlatformApiFactory.ReadData(list)).GetProperty("items").EnumerateArray().ToList();
        var ids = items.Select(item => item.GetProperty("id").GetGuid()).ToList();

        ids.ShouldContain(userId);
        ids.ShouldContain(memberId);
        ids.ShouldNotContain(otherId);
        items.First(item => item.GetProperty("id").GetGuid() == memberId).GetProperty("email").GetString().ShouldBe(memberEmail);
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var (accessToken, _) = await LoginWithTokensAsync(email, password);
        return accessToken;
    }

    private async Task<(Guid UserId, string AccessToken, string RefreshToken)> RegisterLoginWithTokensAsync(string email)
    {
        var register = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var (accessToken, refreshToken) = await LoginWithTokensAsync(email, Password);
        return (userId, accessToken, refreshToken);
    }

    private async Task<(string AccessToken, string RefreshToken)> LoginWithTokensAsync(
        string email,
        string password)
    {
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var data = await PlatformApiFactory.ReadData(login);
        return (data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }

    private async Task<(string AccessToken, DateTimeOffset AccessTokenExpiresAt)> LoginWithTokenExpiryAsync(
        string email,
        string password)
    {
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password });
        login.EnsureSuccessStatusCode();
        var data = await PlatformApiFactory.ReadData(login);
        return (
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("accessTokenExpiresAt").GetDateTimeOffset());
    }

    private async Task<(Guid TenantId, string Subdomain)> ProvisionTenantAsync(string platformAdminToken)
    {
        var subdomain = $"id{Guid.CreateVersion7():N}".Substring(0, 20);
        var provision = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            "/v1/tenant/admin/tenants",
            platformAdminToken,
            new { name = "Identity tenant", subdomain }));
        provision.StatusCode.ShouldBe(HttpStatusCode.OK);

        var data = await PlatformApiFactory.ReadData(provision);
        return (data.GetProperty("tenantId").GetGuid(), subdomain);
    }

    private async Task<string> CreateInviteAsync(string platformAdminToken, Guid tenantId)
    {
        var invite = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post,
            $"/v1/tenant/admin/tenants/{tenantId}/invites",
            platformAdminToken,
            new { expiresInDays = 7 }));
        invite.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await PlatformApiFactory.ReadData(invite)).GetProperty("inviteToken").GetString()!;
    }

    private async Task<Guid> RegisterOnTenantHostAsync(string email, string host, string inviteToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/identity/users")
        {
            Content = JsonContent.Create(new { email, password = Password, inviteToken }),
        };
        request.Headers.Host = host;

        var response = await fixture.Client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await PlatformApiFactory.ReadData(response)).GetProperty("userId").GetGuid();
    }

    /// <summary>
    /// Returns a token for the configured admin. The admin email is a SHARED identity across the collection's DB, so
    /// it is registered only if absent (a 409 from another test that already created it is fine), then logged in
    /// (login bootstraps the admin role from <c>Identity:Auth:AdminEmails</c>). Robust to test execution order.
    /// </summary>
    private async Task<string> EnsureAdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        return await LoginAsync(PlatformApiFactory.AdminEmail, Password);
    }

    /// <summary>All values of a (possibly multi-valued) JWT claim — JsonWebTokenHandler emits one value as a scalar, many as an array.</summary>
    private static IReadOnlyList<string> ClaimValues(string jwt, string claim)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=').Replace('-', '+').Replace('_', '/');
        var json = JsonSerializer.Deserialize<JsonElement>(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));

        if (!json.TryGetProperty(claim, out var element))
        {
            return [];
        }

        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(e => e.GetString()!).ToList()
            : [element.GetString()!];
    }
}
