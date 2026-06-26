using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Credits are the monetized product (sold only through Stripe — package checkout / subscription invoices).
/// The <c>CreditTopUpCommand</c> is an INTERNAL idempotent grant primitive that the saga, the subscription
/// grant and the webhook router dispatch AFTER a real payment. The HTTP endpoint <c>POST /billing/credits/topup</c>
/// must therefore NOT let an ordinary authenticated user mint themselves credits for free — it is an
/// operator/admin hand-grant gated on <c>billing.manage</c>, exactly like the package-catalogue admin writes.
/// </summary>
[Collection("Integration")]
public sealed class CreditTopUpAuthorizationTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Public_topup_endpoint_rejects_a_non_admin_user()
    {
        var (_, userToken) = await fixture.RegisterAndLoginAsync(
            $"minter-{Guid.CreateVersion7():N}@test.io", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/topup", userToken,
            new { amount = 1_000_000L, bucketExpiryDays = (int?)null, idempotencyKey = Guid.CreateVersion7().ToString() }));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden,
            "a non-admin user must not be able to mint themselves the monetized product");
    }

    [Fact]
    public async Task Admin_can_still_hand_grant_credits_via_topup()
    {
        var adminToken = await EnsureAdminAsync();

        var topUp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/topup", adminToken,
            new { amount = 750L, bucketExpiryDays = (int?)null, idempotencyKey = Guid.CreateVersion7().ToString() }));

        topUp.StatusCode.ShouldBe(HttpStatusCode.OK, "an operator with billing.manage may hand-grant credits");
        var data = await PlatformApiFactory.ReadData(topUp);
        data.GetProperty("alreadyApplied").GetBoolean().ShouldBeFalse();
        data.GetProperty("posted").GetInt64().ShouldBeGreaterThanOrEqualTo(750L);
    }

    [Theory]
    [InlineData(0L, "valid-key")]
    [InlineData(-1L, "valid-key")]
    [InlineData(100L, "")]
    public async Task Admin_topup_rejects_invalid_amount_or_missing_idempotency_key(long amount, string idempotencyKey)
    {
        var adminToken = await EnsureAdminAsync();

        var topUp = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/topup", adminToken,
            new { amount, bucketExpiryDays = (int?)null, idempotencyKey }));

        topUp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Admin_topup_with_same_idempotency_key_is_exactly_once()
    {
        var adminToken = await EnsureAdminAsync();
        var key = $"uc29-{Guid.CreateVersion7():N}";

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/topup", adminToken,
            new { amount = 125L, bucketExpiryDays = (int?)null, idempotencyKey = key }));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstData = await PlatformApiFactory.ReadData(first);
        firstData.GetProperty("alreadyApplied").GetBoolean().ShouldBeFalse();
        var accountId = firstData.GetProperty("accountId").GetGuid();
        var posted = firstData.GetProperty("posted").GetInt64();

        var second = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/topup", adminToken,
            new { amount = 125L, bucketExpiryDays = (int?)null, idempotencyKey = key }));
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondData = await PlatformApiFactory.ReadData(second);

        secondData.GetProperty("alreadyApplied").GetBoolean().ShouldBeTrue();
        secondData.GetProperty("posted").GetInt64().ShouldBe(posted);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"IdempotencyKey\" = 'client:{key}'"))
            .ShouldBe(1);
    }

    /// <summary>Admin bootstrap: register (tolerating "already exists") + login; role granted via AdminEmails.</summary>
    private async Task<string> EnsureAdminAsync()
    {
        await fixture.Client.PostAsJsonAsync(
            "/v1/identity/users", new { email = PlatformApiFactory.AdminEmail, password = Password });
        var login = await fixture.Client.PostAsJsonAsync(
            "/v1/identity/auth/login", new { email = PlatformApiFactory.AdminEmail, password = Password });
        login.IsSuccessStatusCode.ShouldBeTrue($"admin login failed: {(int)login.StatusCode}");
        return (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;
    }
}
