using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC30: reserve credits is the only safe "pay tokens" entry point for a module like CRM. It performs the atomic
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
}
