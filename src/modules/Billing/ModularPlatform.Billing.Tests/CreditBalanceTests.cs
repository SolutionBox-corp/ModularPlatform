using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Features.Credits.GetCreditBalance;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC27: credit balance is a read-only, token-scoped projection. UI may display it, but paid backend actions still use
/// reserve/confirm/release commands; the balance endpoint just returns the stored projection those commands maintain.
/// </summary>
[Collection("Integration")]
public sealed class CreditBalanceTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Fresh_user_gets_a_provisioned_zero_balance()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"balance-fresh-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.WaitForCountAsync($"SELECT count(*)::bigint FROM credit_accounts WHERE \"UserId\" = '{userId}'", 1);

        var balance = await GetBalanceAsync(token);

        balance.GetProperty("userId").GetGuid().ShouldBe(userId);
        balance.GetProperty("posted").GetInt64().ShouldBe(0);
        balance.GetProperty("pending").GetInt64().ShouldBe(0);
        balance.GetProperty("available").GetInt64().ShouldBe(0);
    }

    [Fact]
    public async Task Missing_account_returns_a_clear_not_found_from_the_query()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var ex = await Should.ThrowAsync<NotFoundException>(
            () => dispatcher.Query(new GetCreditBalanceQuery(Guid.CreateVersion7())));

        ex.ErrorCode.ShouldBe("credit.account_not_found");
    }

    [Fact]
    public async Task Balance_is_token_scoped_and_tracks_reserve_release_confirm()
    {
        var (aliceId, aliceToken) = await fixture.RegisterAndLoginAsync($"balance-alice-{Guid.CreateVersion7():N}@test.io", Password);
        var (bobId, bobToken) = await fixture.RegisterAndLoginAsync($"balance-bob-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(aliceId, 1_000);
        await fixture.GrantCreditsAsync(bobId, 200);

        var initialAlice = await GetBalanceAsync(aliceToken);
        initialAlice.GetProperty("posted").GetInt64().ShouldBe(1_000);
        initialAlice.GetProperty("pending").GetInt64().ShouldBe(0);
        initialAlice.GetProperty("available").GetInt64().ShouldBe(1_000);

        var reserve = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", aliceToken, new { amount = 250 }));
        reserve.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reservationId = (await PlatformApiFactory.ReadData(reserve)).GetProperty("reservationId").GetGuid();

        var afterReserve = await GetBalanceAsync(aliceToken);
        afterReserve.GetProperty("posted").GetInt64().ShouldBe(1_000);
        afterReserve.GetProperty("pending").GetInt64().ShouldBe(250);
        afterReserve.GetProperty("available").GetInt64().ShouldBe(750);

        var release = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/release", aliceToken, new { reservationId }));
        release.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterRelease = await GetBalanceAsync(aliceToken);
        afterRelease.GetProperty("posted").GetInt64().ShouldBe(1_000);
        afterRelease.GetProperty("pending").GetInt64().ShouldBe(0);
        afterRelease.GetProperty("available").GetInt64().ShouldBe(1_000);

        var reserveForConfirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", aliceToken, new { amount = 300 }));
        reserveForConfirm.StatusCode.ShouldBe(HttpStatusCode.OK);
        var confirmedReservationId = (await PlatformApiFactory.ReadData(reserveForConfirm)).GetProperty("reservationId").GetGuid();
        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", aliceToken, new { reservationId = confirmedReservationId }));
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        var afterConfirm = await GetBalanceAsync(aliceToken);
        afterConfirm.GetProperty("posted").GetInt64().ShouldBe(700);
        afterConfirm.GetProperty("pending").GetInt64().ShouldBe(0);
        afterConfirm.GetProperty("available").GetInt64().ShouldBe(700);

        var bobBalance = await GetBalanceAsync(bobToken);
        bobBalance.GetProperty("userId").GetGuid().ShouldBe(bobId);
        bobBalance.GetProperty("posted").GetInt64().ShouldBe(200);
        bobBalance.GetProperty("available").GetInt64().ShouldBe(200);
    }

    private async Task<System.Text.Json.JsonElement> GetBalanceAsync(string token)
    {
        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/billing/credits/balance", token));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await PlatformApiFactory.ReadData(response);
    }
}
