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
}
