using System.Net;
using System.Net.Http.Json;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC39: subscription plans are server config exposed through a stable public contract. Stripe price ids stay
/// server-side; product-module frontends use planKey and visible grant fields only.
/// </summary>
[Collection("Integration")]
public sealed class SubscriptionPlansTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Subscription_plans_return_enabled_valid_plans_without_provider_price_ids()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"uc39-plans-{Guid.CreateVersion7():N}@example.test", Password);

        using var host = fixture.CreateHost(
            ("Billing:Subscriptions:Plans:1:PlanKey", "disabled"),
            ("Billing:Subscriptions:Plans:1:StripePriceId", "price_disabled"),
            ("Billing:Subscriptions:Plans:1:CreditsPerPeriod", "999"),
            ("Billing:Subscriptions:Plans:1:Enabled", "false"),
            ("Billing:Subscriptions:Plans:2:PlanKey", "broken"),
            ("Billing:Subscriptions:Plans:2:CreditsPerPeriod", "500"),
            ("Billing:Subscriptions:Plans:2:Enabled", "true"),
            ("Billing:Subscriptions:Plans:3:PlanKey", "alpha"),
            ("Billing:Subscriptions:Plans:3:StripePriceId", "price_alpha"),
            ("Billing:Subscriptions:Plans:3:CreditsPerPeriod", "50"),
            ("Billing:Subscriptions:Plans:3:BucketExpiryDays", "30"),
            ("Billing:Subscriptions:Plans:3:Enabled", "true"));
        using var client = host.CreateClient();

        var response = await client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/subscriptions/plans", token));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain("stripePriceId");

        var plans = (await PlatformApiFactory.ReadData(response)).EnumerateArray().ToList();
        plans.Select(p => p.GetProperty("planKey").GetString()).ShouldBe(["alpha", "pro"]);
        plans.Single(p => p.GetProperty("planKey").GetString() == "alpha")
            .GetProperty("bucketExpiryDays").GetInt32().ShouldBe(30);
        plans.Single(p => p.GetProperty("planKey").GetString() == "pro")
            .GetProperty("creditsPerPeriod").GetInt64().ShouldBe(100);
    }

    [Fact]
    public async Task Subscription_plans_return_empty_list_when_config_has_no_enabled_valid_plan()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"uc39-empty-{Guid.CreateVersion7():N}@example.test", Password);

        using var host = fixture.CreateHost(
            ("Billing:Subscriptions:Plans:0:Enabled", "false"),
            ("Billing:Subscriptions:Plans:1:PlanKey", "missing-price"),
            ("Billing:Subscriptions:Plans:1:CreditsPerPeriod", "100"),
            ("Billing:Subscriptions:Plans:1:Enabled", "true"),
            ("Billing:Subscriptions:Plans:2:PlanKey", "zero-credits"),
            ("Billing:Subscriptions:Plans:2:StripePriceId", "price_zero"),
            ("Billing:Subscriptions:Plans:2:CreditsPerPeriod", "0"),
            ("Billing:Subscriptions:Plans:2:Enabled", "true"));
        using var client = host.CreateClient();

        var response = await client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/subscriptions/plans", token));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(response)).GetArrayLength().ShouldBe(0);
    }
}
