using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

[Collection("Integration")]
public sealed class PasswordResetTests(PlatformApiFactory fixture)
{
    private const string OldPassword = "Sup3rSecret!";
    private const string NewPassword = "N3wSup3rSecret!";

    [Fact]
    public async Task Forgot_password_returns_same_accepted_response_for_existing_and_unknown_email()
    {
        var email = $"forgot-existing-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, OldPassword);

        var existing = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/forgot-password", new { email });
        var unknown = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/forgot-password",
            new { email = $"forgot-unknown-{Guid.CreateVersion7():N}@example.com" });

        existing.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        unknown.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var existingBody = await existing.Content.ReadAsStringAsync();
        var unknownBody = await unknown.Content.ReadAsStringAsync();
        existingBody.ShouldContain("\"accepted\":true");
        unknownBody.ShouldBe(existingBody);

        var tokenCount = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM password_reset_tokens WHERE \"UserId\" = '{userId}'");
        tokenCount.ShouldBe(1);
    }

    [Fact]
    public async Task Forgot_password_stores_only_token_hash_not_raw_token()
    {
        var email = $"forgot-hash-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, OldPassword);

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/forgot-password", new { email });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var tokenHash = await fixture.ScalarAsync<string>(
            $"SELECT \"TokenHash\" FROM password_reset_tokens WHERE \"UserId\" = '{userId}' ORDER BY \"CreatedAt\" DESC LIMIT 1");

        tokenHash.ShouldNotBeNullOrWhiteSpace();
        tokenHash.Length.ShouldBe(64);
        tokenHash.ShouldNotContain("http");
        tokenHash.ShouldNotContain("token=");
    }

    [Fact]
    public async Task Forgot_password_for_soft_deleted_account_is_neutral_and_creates_no_token()
    {
        var email = $"forgot-deleted-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, OldPassword);
        await fixture.ExecuteSqlAsync($"UPDATE users SET \"DeletedAt\" = NOW() WHERE \"Id\" = '{userId}'");

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/forgot-password", new { email });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"accepted\":true");
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM password_reset_tokens WHERE \"UserId\" = '{userId}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Reset_password_with_valid_token_changes_password_consumes_tokens_and_revokes_sessions()
    {
        var email = $"reset-success-{Guid.CreateVersion7():N}@example.com";
        var (userId, refreshToken) = await RegisterLoginWithRefreshAsync(email, OldPassword);
        var rawToken = $"reset-{Guid.CreateVersion7():N}";
        await InsertResetTokenAsync(userId, rawToken, expiresMinutesFromNow: 30);

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var consumedCount = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM password_reset_tokens WHERE \"UserId\" = '{userId}' AND \"ConsumedAt\" IS NOT NULL");
        consumedCount.ShouldBe(1);

        var refresh = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/refresh", new { refreshToken });
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var oldLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = OldPassword });
        oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var newLogin = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login",
            new { email, password = NewPassword });
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_password_rejects_the_current_password_without_consuming_the_token()
    {
        var email = $"reset-unchanged-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, OldPassword);
        var rawToken = $"reset-unchanged-{Guid.CreateVersion7():N}";
        await InsertResetTokenAsync(userId, rawToken, expiresMinutesFromNow: 30);

        var unchanged = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = rawToken, newPassword = OldPassword });

        unchanged.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await unchanged.Content.ReadAsStringAsync()).ShouldContain("user.password_unchanged");
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM password_reset_tokens WHERE \"UserId\" = '{userId}' AND \"ConsumedAt\" IS NOT NULL"))
            .ShouldBe(0);

        var retryWithDifferentPassword = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });
        retryWithDifferentPassword.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reset_password_consumes_all_other_outstanding_tokens_for_the_user()
    {
        var email = $"reset-all-tokens-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, OldPassword);
        var firstRawToken = $"reset-first-{Guid.CreateVersion7():N}";
        var secondRawToken = $"reset-second-{Guid.CreateVersion7():N}";
        await InsertResetTokenAsync(userId, firstRawToken, expiresMinutesFromNow: 30);
        await InsertResetTokenAsync(userId, secondRawToken, expiresMinutesFromNow: 30);

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = firstRawToken, newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM password_reset_tokens WHERE \"UserId\" = '{userId}' AND \"ConsumedAt\" IS NOT NULL"))
            .ShouldBe(2);

        var secondLink = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = secondRawToken, newPassword = "AnotherN3wSecret!" });
        secondLink.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await secondLink.Content.ReadAsStringAsync()).ShouldContain("auth.password_reset_invalid");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reset_password_rejects_expired_or_consumed_token(bool expired)
    {
        var email = $"reset-invalid-{expired}-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, OldPassword);
        var rawToken = $"reset-invalid-{Guid.CreateVersion7():N}";
        await InsertResetTokenAsync(userId, rawToken, expiresMinutesFromNow: expired ? -1 : 30, consumed: !expired);

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await response.Content.ReadAsStringAsync()).ShouldContain("auth.password_reset_invalid");
    }

    [Fact]
    public async Task Reset_password_rejects_reused_token()
    {
        var email = $"reset-reuse-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, OldPassword);
        var rawToken = $"reset-reuse-{Guid.CreateVersion7():N}";
        await InsertResetTokenAsync(userId, rawToken, expiresMinutesFromNow: 30);

        var first = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword });
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var second = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/reset-password",
            new { token = rawToken, newPassword = "An0therSecret!" });

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await second.Content.ReadAsStringAsync()).ShouldContain("auth.password_reset_invalid");
    }

    private async Task<(Guid UserId, string RefreshToken)> RegisterLoginWithRefreshAsync(string email, string password)
    {
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new { email, password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var login = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var tokens = await PlatformApiFactory.ReadData(login);

        return (userId, tokens.GetProperty("refreshToken").GetString()!);
    }

    private async Task InsertResetTokenAsync(
        Guid userId,
        string rawToken,
        int expiresMinutesFromNow,
        bool consumed = false)
    {
        var tokenId = Guid.CreateVersion7();
        var hash = HashToken(rawToken);
        var consumedSql = consumed ? "NOW()" : "NULL";

        await fixture.ExecuteSqlAsync(
            "INSERT INTO password_reset_tokens " +
            "(\"Id\", \"UserId\", \"TokenHash\", \"ExpiresAt\", \"ConsumedAt\", \"CreatedAt\", \"UpdatedAt\") VALUES " +
            $"('{tokenId}', '{userId}', '{hash}', NOW() + INTERVAL '{expiresMinutesFromNow} minutes', {consumedSql}, NOW(), NULL)");
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }

}
