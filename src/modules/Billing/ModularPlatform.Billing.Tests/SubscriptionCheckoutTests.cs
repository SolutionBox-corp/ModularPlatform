using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC40: subscription checkout creates a Stripe checkout session only. The local subscription mirror appears later
/// from Stripe object-state webhooks/reconciliation.
/// </summary>
[Collection("Integration")]
public sealed class SubscriptionCheckoutTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Subscription_checkout_rejects_unknown_or_disabled_plan()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"uc40-plan-{Guid.CreateVersion7():N}@example.test", Password);

        var unknown = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/checkout", token, new { planKey = "missing" }));
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await unknown.Content.ReadAsStringAsync()).ShouldContain("billing.subscription.plan_not_found");

        using var host = fixture.CreateHost(
            ("Billing:Subscriptions:Plans:1:PlanKey", "disabled"),
            ("Billing:Subscriptions:Plans:1:StripePriceId", "price_disabled"),
            ("Billing:Subscriptions:Plans:1:CreditsPerPeriod", "100"),
            ("Billing:Subscriptions:Plans:1:Enabled", "false"));
        using var client = host.CreateClient();

        var disabled = await client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/checkout", token, new { planKey = "disabled" }));
        disabled.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await disabled.Content.ReadAsStringAsync()).ShouldContain("billing.subscription.plan_not_found");
    }

    [Fact]
    public async Task Subscription_checkout_rejects_user_with_existing_live_subscription()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"uc40-active-{Guid.CreateVersion7():N}@example.test", Password);
        var subscriptionId = $"sub_{Guid.CreateVersion7():N}";
        Fake.SeedSubscription(new StripeSubscriptionState(
            subscriptionId,
            Status: "active",
            CustomerId: "cus_test",
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddMonths(1),
            CancelAtPeriodEnd: false,
            Metadata: new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));
        await DispatchAsync(new UpsertSubscriptionFromStripeCommand(subscriptionId));

        var checkout = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/checkout", token, new { planKey = "pro" }));

        checkout.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await checkout.Content.ReadAsStringAsync()).ShouldContain("billing.subscription.already_active");
    }

    [Fact]
    public async Task Double_subscription_checkout_creates_two_provider_sessions_but_no_local_pending_subscription()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"uc40-double-{Guid.CreateVersion7():N}@example.test", Password);

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/checkout", token, new { planKey = "pro" }));
        var second = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/checkout", token, new { planKey = "pro" }));

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstSession = (await PlatformApiFactory.ReadData(first)).GetProperty("checkoutSessionId").GetString();
        var secondSession = (await PlatformApiFactory.ReadData(second)).GetProperty("checkoutSessionId").GetString();
        secondSession.ShouldNotBe(firstSession);

        var localRows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM subscriptions WHERE \"UserId\" = '{userId}'");
        localRows.ShouldBe(0);
    }

    [Fact]
    public async Task Subscription_checkout_propagates_automatic_tax_config_to_provider_session()
    {
        using var host = fixture.CreateHost(("Billing:Stripe:AutomaticTax", "true"));
        using var client = host.CreateClient();
        var fake = (FakeStripeGateway)host.Services.GetRequiredService<IStripeGateway>();
        var (_, token) = await RegisterAndLoginAsync(
            client, $"uc40-tax-{Guid.CreateVersion7():N}@example.test");

        var checkout = await client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/checkout", token, new { planKey = "pro" }));

        checkout.StatusCode.ShouldBe(HttpStatusCode.OK);
        fake.CreatedSessions.ShouldContain(s =>
            s.Mode == "subscription"
            && s.PriceId == "price_test_pro"
            && s.AutomaticTax);
    }

    private FakeStripeGateway Fake => (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();

    private async Task DispatchAsync(ICommand command)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(command);
    }

    private static async Task<(Guid UserId, string AccessToken)> RegisterAndLoginAsync(HttpClient client, string email)
    {
        var register = await client.PostAsJsonAsync("/v1/identity/users", new { email, password = Password });
        register.StatusCode.ShouldBe(HttpStatusCode.Created);
        var userId = (await PlatformApiFactory.ReadData(register)).GetProperty("userId").GetGuid();

        var login = await client.PostAsJsonAsync("/v1/identity/auth/login", new { email, password = Password });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var token = (await PlatformApiFactory.ReadData(login)).GetProperty("accessToken").GetString()!;

        return (userId, token);
    }
}
