using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Identity.Tests;

[Collection("Integration")]
public sealed class EmailVerificationTests(PlatformApiFactory fixture)
{
    private const string Password = "Sup3rSecret!";

    [Fact]
    public async Task Register_creates_unverified_user_and_hash_only_verification_token()
    {
        var email = $"verify-register-{Guid.CreateVersion7():N}@example.com";
        var register = await fixture.Client.PostAsJsonAsync("/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var confirmed = await fixture.ScalarAsync<bool>(
            $"SELECT \"EmailConfirmed\" FROM users WHERE \"Id\" = '{userId}'");
        confirmed.ShouldBeFalse();

        var tokenHash = await fixture.ScalarAsync<string>(
            $"SELECT \"TokenHash\" FROM email_verification_tokens WHERE \"UserId\" = '{userId}' ORDER BY \"CreatedAt\" DESC LIMIT 1");
        tokenHash.Length.ShouldBe(64);
        tokenHash.ShouldNotContain("token=");
    }

    [Fact]
    public async Task Profile_exposes_email_confirmation_status()
    {
        var email = $"verify-profile-{Guid.CreateVersion7():N}@example.com";
        var (_, accessToken) = await fixture.RegisterAndLoginAsync(email, Password);

        var profile = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/identity/users/me", accessToken));

        profile.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(profile);
        data.GetProperty("emailConfirmed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task Verify_email_with_valid_token_marks_user_confirmed_and_consumes_tokens()
    {
        var email = $"verify-success-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var rawToken = $"verify-{Guid.CreateVersion7():N}";
        await InsertVerificationTokenAsync(userId, rawToken, expiresMinutesFromNow: 30);

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/verify-email", new { token = rawToken });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var confirmed = await fixture.ScalarAsync<bool>(
            $"SELECT \"EmailConfirmed\" FROM users WHERE \"Id\" = '{userId}'");
        confirmed.ShouldBeTrue();

        var consumed = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM email_verification_tokens WHERE \"UserId\" = '{userId}' AND \"ConsumedAt\" IS NOT NULL");
        consumed.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Verify_email_consumes_all_other_outstanding_tokens_for_the_user()
    {
        var email = $"verify-all-tokens-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var firstRawToken = $"verify-first-{Guid.CreateVersion7():N}";
        var secondRawToken = $"verify-second-{Guid.CreateVersion7():N}";
        await InsertVerificationTokenAsync(userId, firstRawToken, expiresMinutesFromNow: 30);
        await InsertVerificationTokenAsync(userId, secondRawToken, expiresMinutesFromNow: 30);

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/verify-email", new { token = firstRawToken });

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM email_verification_tokens WHERE \"UserId\" = '{userId}' AND \"ConsumedAt\" IS NOT NULL"))
            .ShouldBeGreaterThanOrEqualTo(2);

        var secondLink = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/verify-email", new { token = secondRawToken });
        secondLink.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await secondLink.Content.ReadAsStringAsync()).ShouldContain("auth.email_verification_invalid");
    }

    [Fact]
    public async Task Verify_email_for_deleted_user_is_invalid_and_does_not_confirm_the_user()
    {
        var email = $"verify-deleted-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var rawToken = $"verify-deleted-{Guid.CreateVersion7():N}";
        await InsertVerificationTokenAsync(userId, rawToken, expiresMinutesFromNow: 30);
        await fixture.ExecuteSqlAsync($"UPDATE users SET \"DeletedAt\" = NOW() WHERE \"Id\" = '{userId}'");

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/verify-email", new { token = rawToken });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await response.Content.ReadAsStringAsync()).ShouldContain("auth.email_verification_invalid");
        (await fixture.ScalarAsync<bool>(
            $"SELECT \"EmailConfirmed\" FROM users WHERE \"Id\" = '{userId}'")).ShouldBeFalse();
    }

    [Fact]
    public async Task Reusing_a_verification_link_after_success_does_not_restamp_email_confirmed_at()
    {
        var email = $"verify-restamp-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var rawToken = $"verify-restamp-{Guid.CreateVersion7():N}";
        await InsertVerificationTokenAsync(userId, rawToken, expiresMinutesFromNow: 30);

        var first = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/verify-email", new { token = rawToken });
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var confirmedAt = await fixture.ScalarAsync<string>(
            $"SELECT \"EmailConfirmedAt\"::text FROM users WHERE \"Id\" = '{userId}'");

        var second = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/verify-email", new { token = rawToken });

        second.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await second.Content.ReadAsStringAsync()).ShouldContain("auth.email_verification_invalid");
        (await fixture.ScalarAsync<string>(
            $"SELECT \"EmailConfirmedAt\"::text FROM users WHERE \"Id\" = '{userId}'")).ShouldBe(confirmedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Verify_email_rejects_expired_or_consumed_token(bool expired)
    {
        var email = $"verify-invalid-{expired}-{Guid.CreateVersion7():N}@example.com";
        var (userId, _) = await fixture.RegisterAndLoginAsync(email, Password);
        var rawToken = $"verify-invalid-{Guid.CreateVersion7():N}";
        await InsertVerificationTokenAsync(userId, rawToken, expiresMinutesFromNow: expired ? -1 : 30, consumed: !expired);

        var response = await fixture.Client.PostAsJsonAsync("/v1/identity/auth/verify-email", new { token = rawToken });

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableContent);
        (await response.Content.ReadAsStringAsync()).ShouldContain("auth.email_verification_invalid");
    }

    [Fact]
    public async Task Resend_email_verification_consumes_old_tokens_and_already_verified_is_noop()
    {
        var email = $"verify-resend-{Guid.CreateVersion7():N}@example.com";
        var (userId, accessToken) = await fixture.RegisterAndLoginAsync(email, Password);

        var resend = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/users/me/email-verification", accessToken));
        resend.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var resendData = await PlatformApiFactory.ReadData(resend);
        resendData.GetProperty("accepted").GetBoolean().ShouldBeTrue();
        resendData.GetProperty("alreadyVerified").GetBoolean().ShouldBeFalse();

        var outstanding = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM email_verification_tokens WHERE \"UserId\" = '{userId}' AND \"ConsumedAt\" IS NULL");
        outstanding.ShouldBe(1);

        await fixture.ExecuteSqlAsync(
            $"UPDATE users SET \"EmailConfirmed\" = true, \"EmailConfirmedAt\" = NOW() WHERE \"Id\" = '{userId}'");

        var afterVerified = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/identity/users/me/email-verification", accessToken));
        afterVerified.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var afterData = await PlatformApiFactory.ReadData(afterVerified);
        afterData.GetProperty("alreadyVerified").GetBoolean().ShouldBeTrue();
    }

    private async Task InsertVerificationTokenAsync(
        Guid userId,
        string rawToken,
        int expiresMinutesFromNow,
        bool consumed = false)
    {
        var tokenId = Guid.CreateVersion7();
        var hash = HashToken(rawToken);
        var consumedSql = consumed ? "NOW()" : "NULL";

        await fixture.ExecuteSqlAsync(
            "INSERT INTO email_verification_tokens " +
            "(\"Id\", \"UserId\", \"TokenHash\", \"ExpiresAt\", \"ConsumedAt\", \"CreatedAt\", \"UpdatedAt\") VALUES " +
            $"('{tokenId}', '{userId}', '{hash}', NOW() + INTERVAL '{expiresMinutesFromNow} minutes', {consumedSql}, NOW(), NULL)");
    }

    private static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes);
    }
}
