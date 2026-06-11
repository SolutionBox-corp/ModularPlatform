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
        var (userId, _, refreshToken) = await RegisterLoginAsync();

        // Soft-delete the account directly (isolates the refresh-path guard from the async erasure flow).
        await fixture.ExecuteSqlAsync(
            $"UPDATE users SET \"DeletedAt\" = now() WHERE \"Id\" = '{userId}'");

        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken });

        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized,
            "a soft-deleted account must not be able to rotate its session");
    }

    [Fact]
    public async Task Erasure_revokes_all_of_the_subjects_refresh_tokens()
    {
        var (userId, accessToken, refreshToken) = await RegisterLoginAsync();

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
    public async Task Logout_revokes_the_session_family()
    {
        var (_, accessToken, refreshToken) = await RegisterLoginAsync();

        var logout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/auth/logout", accessToken, new { refreshToken }));
        logout.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The logged-out token's family is dead — it can no longer rotate.
        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<(Guid UserId, string AccessToken, string RefreshToken)> RegisterLoginAsync()
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
        return (userId, data.GetProperty("accessToken").GetString()!, data.GetProperty("refreshToken").GetString()!);
    }
}
