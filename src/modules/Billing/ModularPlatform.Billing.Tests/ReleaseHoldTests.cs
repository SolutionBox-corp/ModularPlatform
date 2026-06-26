using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC32: release hold is the failure/reconcile branch for reserved credits. It must be idempotent and must never
/// restore credits after the hold has already been confirmed into a real spend.
/// </summary>
[Collection("Integration")]
public sealed class ReleaseHoldTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Release_rejects_missing_reservation_id_before_handler()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"release-empty-{Guid.CreateVersion7():N}@test.io", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/release", token,
            new { reservationId = Guid.Empty }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Release_unknown_reservation_is_a_404()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"release-missing-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(userId, 100);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/release", token,
            new { reservationId = Guid.CreateVersion7() }));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Release_after_confirm_does_not_restore_spent_credits()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"release-confirmed-{Guid.CreateVersion7():N}@test.io", Password);
        await fixture.GrantCreditsAsync(userId, 200);

        var reserve = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations", token, new { amount = 75 }));
        reserve.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reservationId = (await PlatformApiFactory.ReadData(reserve)).GetProperty("reservationId").GetGuid();

        var confirm = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/confirm", token, new { reservationId }));
        confirm.StatusCode.ShouldBe(HttpStatusCode.OK);

        var release = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/credits/reservations/release", token, new { reservationId }));
        release.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(release)).GetProperty("available").GetInt64().ShouldBe(125);

        var projection = await fixture.ScalarAsync<string>(
            $"SELECT \"Posted\" || ':' || \"Pending\" || ':' || \"Available\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        projection.ShouldBe("125:0:125");

        var accountId = await fixture.ScalarAsync<Guid>($"SELECT \"Id\" FROM credit_accounts WHERE \"UserId\" = '{userId}'");
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"AccountId\" = '{accountId}' AND \"Type\" = 'Release'"))
            .ShouldBe(0);
    }
}
