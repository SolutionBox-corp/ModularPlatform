using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC31: confirm spend is called only after the product-module action really succeeded. It converts an active hold into
/// posted spend exactly once; invalid or already-released reservations must not spend anything.
/// </summary>
[Collection("Integration")]
public sealed class ConfirmSpendTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Confirm_rejects_missing_reservation_id_before_handler()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"confirm-empty-{Guid.CreateVersion7():N}@test.io", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token,
            new { reservationId = Guid.Empty }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Confirm_unknown_reservation_is_a_404()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"confirm-missing-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(userId, 100);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token,
            new { reservationId = Guid.CreateVersion7() }));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Confirm_released_reservation_is_rejected_without_spend()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"confirm-released-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(userId, 200);

        var reserve = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount = 75 }));
        reserve.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reservationId = (await PlatformApiFactory.ReadData(reserve)).GetProperty("reservationId").GetGuid();

        var release = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/release", token, new { reservationId }));
        release.StatusCode.ShouldBe(HttpStatusCode.OK);

        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token, new { reservationId }));
        confirm.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await confirm.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("errorCode").GetString().ShouldBe("credit.reservation_not_active");

        var accountId = await fixture.ScalarAsync<Guid>($"SELECT \"Id\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Spend'"))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Confirm_expired_but_unswept_reservation_is_rejected_without_spend()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"confirm-expired-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(userId, 200);

        var reservationId = await ReserveAsync(token, 75);
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_holds SET \"ExpiresAt\" = now() - interval '1 minute' WHERE \"Id\" = '{reservationId}'");

        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token, new { reservationId }));

        confirm.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await confirm.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("errorCode").GetString().ShouldBe("credit.reservation_not_active");

        var accountId = await AccountIdAsync(userId);
        (await SpendCountAsync(accountId)).ShouldBe(0);
    }

    [Fact]
    public async Task Confirm_foreign_reservation_is_a_404_and_does_not_spend()
    {
        var (aliceId, aliceToken) = await fixture.RegisterAndLoginAsync($"confirm-alice-{Guid.CreateVersion7():N}@test.io", Password);
        var (bobId, bobToken) = await fixture.RegisterAndLoginAsync($"confirm-bob-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(aliceId, 100);
        await fixture.GrantCreditsAsync(bobId, 100);

        var aliceReservationId = await ReserveAsync(aliceToken, 40);

        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", bobToken, new { reservationId = aliceReservationId }));

        confirm.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var body = await confirm.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("errorCode").GetString().ShouldBe("credit.reservation_not_found");

        var aliceAccountId = await AccountIdAsync(aliceId);
        var bobAccountId = await AccountIdAsync(bobId);
        (await SpendCountAsync(aliceAccountId)).ShouldBe(0);
        (await SpendCountAsync(bobAccountId)).ShouldBe(0);
    }

    [Fact]
    public async Task Confirm_fails_loudly_when_buckets_no_longer_cover_the_hold()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"confirm-underflow-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(userId, 100);

        var reservationId = await ReserveAsync(token, 80);
        var accountId = await AccountIdAsync(userId);
        await fixture.ExecuteSqlAsync(
            $"UPDATE credit_buckets SET \"Remaining\" = 50 WHERE \"AccountId\" = '{accountId}'");

        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token, new { reservationId }));

        confirm.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var body = await confirm.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        body.GetProperty("errorCode").GetString().ShouldBe("credit.bucket_underflow");

        (await SpendCountAsync(accountId)).ShouldBe(0);
        (await fixture.ScalarAsync<string>($"SELECT \"Status\" FROM credit_holds WHERE \"Id\" = '{reservationId}'"))
            .ShouldBe("Active");
        (await fixture.ScalarAsync<long>($"SELECT \"Posted\" FROM credit_accounts WHERE \"Id\" = '{accountId}'"))
            .ShouldBe(100);
        (await fixture.ScalarAsync<long>($"SELECT \"Pending\" FROM credit_accounts WHERE \"Id\" = '{accountId}'"))
            .ShouldBe(80);
        (await fixture.ScalarAsync<long>($"SELECT \"Available\" FROM credit_accounts WHERE \"Id\" = '{accountId}'"))
            .ShouldBe(20);
    }

    private async Task<Guid> ReserveAsync(string token, long amount)
    {
        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount }));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await PlatformApiFactory.ReadData(response)).GetProperty("reservationId").GetGuid();
    }

    private async Task<Guid> AccountIdAsync(Guid userId)
    {
        return await fixture.ScalarAsync<Guid>($"SELECT \"Id\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
    }

    private async Task<long> SpendCountAsync(Guid accountId)
    {
        return await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Spend'");
    }
}
