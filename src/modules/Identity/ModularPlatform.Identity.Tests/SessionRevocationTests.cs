using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Session-revocation invariants — a refresh token must die when it should:
/// <list type="bullet">
/// <item>a GDPR-erased (soft-deleted) account can no longer mint access tokens (the refresh path rejects
/// <c>DeletedAt != null</c>), AND erasure proactively revokes every outstanding refresh token;</item>
/// <item>an explicit logout revokes the presented token's whole rotation family.</item>
/// </list>
/// Without these, an erased/offboarded user (or a thief holding their refresh token) keeps a perpetually
/// rolling, fully authenticated session.
/// </summary>
[Collection("Integration")]
public sealed class SessionRevocationTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Refresh_is_rejected_when_the_account_is_soft_deleted()
    {
        var (userId, _, refreshToken, _) = await RegisterLoginAsync();

        // Soft-delete the account directly (isolates the refresh-path guard from the async erasure flow).
        await fixture.ExecuteSqlAsync(
            $"UPDATE users SET \"DeletedAt\" = now() WHERE \"Id\" = '{userId}'");

        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken });

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "a soft-deleted account must not be able to rotate its session");
    }

    [Fact]
    public async Task Login_is_rejected_when_the_account_is_soft_deleted()
    {
        var (userId, _, _, email) = await RegisterLoginAsync();

        await fixture.ExecuteSqlAsync(
            $"UPDATE users SET \"DeletedAt\" = now() WHERE \"Id\" = '{userId}'");

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = Password });

        login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "a soft-deleted account must not be able to mint a new access token");
    }

    [Fact]
    public async Task Erasure_revokes_all_of_the_subjects_refresh_tokens()
    {
        var (userId, accessToken, refreshToken, _) = await RegisterLoginAsync();

        // Trigger erasure through the real HTTP route (subject = the authenticated user).
        var erase = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", accessToken));
        erase.EnsureSuccessStatusCode();

        // Durable + async — poll until the crypto-shred has stamped DeletedAt on the subject key.
        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM subject_keys WHERE "UserId" = '{userId}' AND "DeletedAt" IS NOT NULL""", 1);

        // Every refresh token of the erased subject is revoked.
        var stillActive = await fixture.ScalarAsync<long>(
            $"""SELECT count(*)::bigint FROM refresh_tokens WHERE "UserId" = '{userId}' AND "RevokedAt" IS NULL""");
        stillActive.ShouldBe(0, "erasure must revoke every outstanding refresh token");

        // And the token can no longer rotate.
        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Erased_user_access_token_can_no_longer_read_profile()
    {
        var (userId, accessToken, _, _) = await RegisterLoginAsync();

        var erase = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/gdpr/me/erase", accessToken));
        erase.EnsureSuccessStatusCode();

        await fixture.WaitForCountAsync(
            $"""SELECT count(*)::bigint FROM users WHERE "Id" = '{userId}' AND "DeletedAt" IS NOT NULL""", 1);

        var me = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/identity/users/me", accessToken));

        me.StatusCode.ShouldBe(HttpStatusCode.NotFound,
            "an existing access token must not read a GDPR-erased profile");
    }

    [Fact]
    public async Task Logout_revokes_the_session_family()
    {
        var (_, accessToken, refreshToken, _) = await RegisterLoginAsync();

        var logout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/auth/logout", accessToken, new { refreshToken }));
        logout.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The logged-out token's family is dead — it can no longer rotate.
        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_with_another_users_refresh_token_is_a_silent_noop()
    {
        var (_, accessTokenA, _, _) = await RegisterLoginAsync();
        var (_, _, refreshTokenB, _) = await RegisterLoginAsync();

        var logout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/auth/logout", accessTokenA, new { refreshToken = refreshTokenB }));
        logout.StatusCode.ShouldBe(HttpStatusCode.OK);

        var refreshB = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken = refreshTokenB });
        refreshB.StatusCode.ShouldBe(HttpStatusCode.OK,
            "a caller must not be able to revoke another user's refresh token family");
    }

    [Fact]
    public async Task Logout_with_an_unknown_refresh_token_is_a_silent_success()
    {
        var (_, accessToken, _, _) = await RegisterLoginAsync();

        var logout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/auth/logout", accessToken, new { refreshToken = "not-a-real-refresh-token" }));

        logout.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_without_authentication_is_rejected()
    {
        var logout = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/logout",
            new { refreshToken = "anything" });

        logout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_can_revoke_all_refresh_sessions_for_a_user()
    {
        var (userId, accessToken, firstRefreshToken, email) = await RegisterLoginAsync();

        var secondLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = Password });
        secondLogin.EnsureSuccessStatusCode();
        var secondRefreshToken = (await PlatformApiFactory.ReadData(secondLogin)).GetProperty("refreshToken").GetString()!;

        var (_, normalToken, _, _) = await RegisterLoginAsync();
        var forbidden = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/sessions/revoke", normalToken));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var adminToken = await EnsureAdminTokenAsync();
        var revoke = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, $"/v1/identity/admin/users/{userId}/sessions/revoke", adminToken));
        revoke.StatusCode.ShouldBe(HttpStatusCode.OK);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM refresh_tokens WHERE \"UserId\" = '{userId}' AND \"RevokedAt\" IS NULL"))
            .ShouldBe(0);

        var firstRefresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = firstRefreshToken });
        firstRefresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var secondRefresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = secondRefreshToken });
        secondRefresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var existingAccessTokenStillBoundedByExpiry = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/identity/users/me", accessToken));
        existingAccessTokenStillBoundedByExpiry.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<(Guid UserId, string AccessToken, string RefreshToken, string Email)> RegisterLoginAsync()
    {
        var email = $"sess-{Guid.CreateVersion7():N}@example.com";
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var data = await PlatformApiFactory.ReadData(login);
        return (userId, data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!, email);
    }

    private async Task<string> EnsureAdminTokenAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.EnsureSuccessStatusCode();

        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
