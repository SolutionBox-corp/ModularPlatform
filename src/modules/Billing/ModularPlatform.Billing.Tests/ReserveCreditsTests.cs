using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC30: reserve credits is the only safe "pay tokens" entry point for any product module. It performs the atomic
/// balance guard in Billing; callers must never read the balance and then write their own local reservation.
/// </summary>
[Collection("Integration")]
public sealed class ReserveCreditsTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Theory]
    [InlineData(0L, null)]
    [InlineData(-1L, null)]
    [InlineData(100L, 0)]
    [InlineData(100L, -1)]
    public async Task Reserve_endpoint_rejects_invalid_amount_or_hold_minutes(long amount, int? holdMinutes)
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"reserve-invalid-{Guid.CreateVersion7():N}@test.io", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", token,
            new { amount, holdMinutes }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reserve_without_credit_account_returns_not_found()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"reserve-no-account-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);
        await fixture.ExecuteSqlAsync($"DELETE FROM credit_accounts WHERE \"UserId\" = '{userId}'");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", token,
            new { amount = 10 }));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("credit.account_not_found");
    }

    [Fact]
    public async Task Reserve_honors_custom_hold_minutes()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"reserve-custom-hold-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(userId, 100);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", token,
            new { amount = 25, holdMinutes = 90 }));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reservationId = (await PlatformApiFactory.ReadData(response)).GetProperty("reservationId").GetGuid();

        var holdMinutes = await fixture.ScalarAsync<decimal>(
            $"SELECT EXTRACT(EPOCH FROM (\"ExpiresAt\" - \"CreatedAt\")) / 60 FROM credit_holds WHERE \"Id\" = '{reservationId}'");
        holdMinutes.ShouldBe(90m, tolerance: 0.1m);
    }
}
