using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC44: promo-code validation is only a UI pre-check. Stripe Checkout still performs the authoritative validation
/// for applicability, expiry, redemption limits and final discount math.
/// </summary>
[Collection("Integration")]
public sealed class PromoCodeTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Promo_code_validate_returns_discount_shape_for_active_code()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"uc44-valid-{Guid.CreateVersion7():N}@example.test", Password);
        Fake.SeedPromotionCode(new PromotionCodeState("SUMMER10", PercentOff: 10m, AmountOff: null, Currency: null));

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/promo-codes/SUMMER10/validate", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("code").GetString().ShouldBe("SUMMER10");
        data.GetProperty("percentOff").GetDecimal().ShouldBe(10m);
    }

    [Fact]
    public async Task Promo_code_validate_trims_ui_input_before_provider_lookup()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"uc44-trim-{Guid.CreateVersion7():N}@example.test", Password);
        Fake.SeedPromotionCode(new PromotionCodeState("WELCOME5", PercentOff: null, AmountOff: 500, Currency: "eur"));

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/promo-codes/%20WELCOME5%20/validate", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("amountOff").GetInt64().ShouldBe(500);
        data.GetProperty("currency").GetString().ShouldBe("eur");
    }

    [Fact]
    public async Task Promo_code_validate_returns_404_for_invalid_or_expired_code()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"uc44-invalid-{Guid.CreateVersion7():N}@example.test", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/promo-codes/EXPIRED/validate", token));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.coupon.invalid");
    }

    [Fact]
    public async Task Promo_code_validate_translates_provider_rate_limit_to_domain_error()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"uc44-provider-{Guid.CreateVersion7():N}@example.test", Password);
        Fake.FailNextPromotionCodeLookup();

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/promo-codes/SUMMER10/validate", token));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.coupon.provider_failed");
    }

    private FakeStripeGateway Fake => (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();
}
