using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// The original end-to-end identity flow (ID-1/ID-5/ID-12 smoke), now on the SHARED harness (Law 9).
/// The previous private ApiFixture (own Testcontainer + host) was a legacy deviation — and with PII column
/// encryption it became actively harmful: a second host in the process re-points the process-wide
/// personal-data protector at a different database, breaking decryption for every other test.
/// ONE host per test process is now a hard harness invariant.
/// </summary>
[Collection("Integration")]
public sealed class IdentityE2ETests(PlatformApiFactory fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Register_login_refresh_rotation_reuse_detection_and_profile()
    {
        var email = $"user-{Guid.CreateVersion7():N}@example.com";

        // Register
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password = "Sup3rSecret!", displayName = "Test User" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        // Login
        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = "Sup3rSecret!" });
        login.EnsureSuccessStatusCode();
        var tokens = await Unwrap<Tokens>(login);
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();

        // Profile with the access token
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/identity/users/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        var me = await fixture.Client.SendAsync(meRequest);
        me.EnsureSuccessStatusCode();
        var profile = await Unwrap<Profile>(me);
        profile.Email.ShouldBe(email);

        // Refresh rotation: the old refresh token is consumed and replaced
        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = tokens.RefreshToken });
        refresh.EnsureSuccessStatusCode();
        var rotated = await Unwrap<Tokens>(refresh);
        rotated.RefreshToken.ShouldNotBe(tokens.RefreshToken);

        // REUSE DETECTION: replaying the consumed token must be rejected (401)
        var reuse = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = tokens.RefreshToken });
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Audit: registering THIS user wrote a Create row (only changed columns, JSONB).
        var auditCount = await fixture.ScalarAsync<long>(
            "SELECT count(*)::bigint FROM identity_audit_entries WHERE \"Action\" = 'Create' " +
            $"AND \"EntityType\" = 'User' AND \"EntityId\" = '{userId}'");
        auditCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    private static async Task<T> Unwrap<T>(HttpResponseMessage response)
    {
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<T>>(Json);
        envelope.ShouldNotBeNull();
        return envelope!.Data;
    }

    private sealed record ApiEnvelope<T>(T Data, string? Message, bool Success);
    private sealed record Tokens(string AccessToken, DateTimeOffset AccessTokenExpiresAt, string RefreshToken);
    private sealed record Profile(Guid Id, string Email, string? DisplayName, string Locale);
}
