using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

/// <summary>
/// Auth robustness on the shared integration host (real Postgres + full Api, RLS on, Solo durability):
/// duplicate-email conflict (ID-2), refresh-reuse detection + family revoke is AUDITED (ID-6), and two
/// parallel refreshes of the same token resolve to exactly one winner with no 5xx (ID-8).
/// Routes/codes verified against production:
/// register POST /v1/identity/users -> ConflictException("user.email_taken") (RegisterUserEndpoint.cs:18, RegisterUserHandler.cs:32);
/// login POST /v1/identity/auth/login (LoginEndpoint.cs:13); refresh POST /v1/identity/auth/refresh body { refreshToken }
/// -> UnauthorizedException("auth.refresh_token_reused") (RefreshTokenEndpoint.cs:13, RefreshTokenCommand.cs:7, RefreshTokenHandler.cs:56).
/// Tables: users (NormalizedEmail), refresh_tokens (FamilyId/RevokedAt, RefreshToken.cs:30), identity_audit_entries
/// (AuditEntry.EntityType=ClrType.Name "RefreshToken", Action "Update"; AuditInterceptor.cs:91).
/// </summary>
[Collection("Integration")]
public sealed class AuthRobustnessTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    // ID-2 — registering the SAME email twice: the second attempt is 409 user.email_taken and only ONE user row exists.
    [Fact]
    public async Task Duplicate_email_registration_is_conflict_and_creates_exactly_one_user()
    {
        var email = $"dup-{Guid.CreateVersion7():N}@example.com";

        var first = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password = Password, displayName = "Dup User" });
        first.StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password = Password, displayName = "Dup User Again" });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var body = await second.Content.ReadAsStringAsync();
        body.ShouldContain("user.email_taken");

        // Exactly one user row for this email. Email is stored case-preserving + a NormalizedEmail (upper);
        // uniqueness is enforced on NormalizedEmail (RegisterUserHandler.cs:30).
        var normalized = email.ToUpperInvariant();
        var count = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM users WHERE \"NormalizedEmail\" = '{normalized}'");
        count.ShouldBe(1);
    }

    // ID-6 — refresh-reuse is rejected AND audited: rotate once, replay the OLD token -> 401 auth.refresh_token_reused;
    // every token in the family ends with RevokedAt set, and the revoke produced >=1 RefreshToken Update audit row.
    [Fact]
    public async Task Refresh_reuse_revokes_whole_family_and_is_audited()
    {
        var email = $"reuse-{Guid.CreateVersion7():N}@example.com";
        var (userId, originalRefresh) = await RegisterAndLoginForRefreshAsync(email);

        // Rotate once using the original token (consumes it, issues a replacement in the same family).
        var rotate = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = originalRefresh });
        rotate.EnsureSuccessStatusCode();
        var rotated = await PlatformApiFactory.ReadData(rotate);
        var newRefresh = rotated.GetProperty("refreshToken").GetString()!;
        newRefresh.ShouldNotBe(originalRefresh);

        // Replay the now-consumed ORIGINAL token -> reuse detected -> 401 with the security error code.
        var reuse = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh",
            new { refreshToken = originalRefresh });
        reuse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await reuse.Content.ReadAsStringAsync()).ShouldContain("auth.refresh_token_reused");

        // (a) The whole family is revoked: no row for this user has a NULL RevokedAt.
        // The revoke is a tracked SaveChanges in the handler (RefreshTokenHandler.cs:45-54); poll until it lands.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM refresh_tokens WHERE \"UserId\" = '{userId}' AND \"RevokedAt\" IS NULL",
            0);
        var familyTotal = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM refresh_tokens WHERE \"UserId\" = '{userId}'");
        familyTotal.ShouldBeGreaterThanOrEqualTo(2); // original + replacement
        var stillActive = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM refresh_tokens WHERE \"UserId\" = '{userId}' AND \"RevokedAt\" IS NULL");
        stillActive.ShouldBe(0);

        // (b) The revoke is audited: >=1 RefreshToken 'Update' audit row whose EntityId is one of this user's tokens.
        await fixture.WaitForCountAsync(
            "SELECT count(*)::bigint FROM identity_audit_entries a " +
            "WHERE a.\"EntityType\" = 'RefreshToken' AND a.\"Action\" = 'Update' " +
            $"AND a.\"EntityId\" IN (SELECT \"Id\"::text FROM refresh_tokens WHERE \"UserId\" = '{userId}')",
            1);
    }

    // ID-8 — two PARALLEL /refresh with the SAME valid token: exactly one 200, the other 401, family ends revoked,
    // and NEITHER response is a 5xx (concurrency must be handled, never an unhandled server error).
    [Fact]
    public async Task Parallel_refresh_with_same_token_yields_one_winner_no_server_error()
    {
        var email = $"parallel-{Guid.CreateVersion7():N}@example.com";
        var (userId, refresh) = await RegisterAndLoginForRefreshAsync(email);

        var firstTask = fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken = refresh });
        var secondTask = fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken = refresh });
        var responses = await Task.WhenAll(firstTask, secondTask);

        // No unhandled 500 on either branch.
        foreach (var response in responses)
        {
            ((int)response.StatusCode).ShouldBeLessThan(500);
        }

        var statuses = responses.Select(r => r.StatusCode).ToArray();
        statuses.Count(s => s == HttpStatusCode.OK).ShouldBe(1);
        statuses.Count(s => s == HttpStatusCode.Unauthorized).ShouldBe(1);

        // The family ends revoked: the loser's reuse detection compromises the family, so once both calls have
        // resolved (one 200 + one 401-reuse) every token for this user is revoked. Poll for the async SaveChanges.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM refresh_tokens WHERE \"UserId\" = '{userId}' AND \"RevokedAt\" IS NULL",
            0);
    }

    /// <summary>
    /// Registers + logs in directly via the HTTP client (not the harness helper) so we capture the raw REFRESH
    /// token from the login envelope. Returns (userId, refreshToken).
    /// </summary>
    private async Task<(Guid UserId, string RefreshToken)> RegisterAndLoginForRefreshAsync(string email)
    {
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users",
            new { email, password = Password, displayName = "Refresh User" });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        var refresh = (await PlatformApiFactory.ReadData(login)).GetProperty("refreshToken").GetString()!;

        return (userId, refresh);
    }
}
