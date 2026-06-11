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
        (await PlatformApiFactory.ReadData(topUp)).GetProperty("posted").GetInt64().ShouldBe(750L);
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
