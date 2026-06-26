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
/// UC41: /subscriptions/me is the UI/API read model for the caller's current subscription. CRM does not keep a local
/// subscription copy; it reads this endpoint or reacts to platform entitlement policy.
/// </summary>
[Collection("Integration")]
public sealed class MySubscriptionTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task My_subscription_returns_404_empty_state_when_user_has_no_live_subscription()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"uc41-empty-{Guid.CreateVersion7():N}@example.test", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/subscriptions/me", token));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.subscription.not_found");
    }

    [Fact]
    public async Task My_subscription_reflects_past_due_and_cancel_at_period_end_from_reconciled_state()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"uc41-state-{Guid.CreateVersion7():N}@example.test", Password);
        var subscriptionId = $"sub_{Guid.CreateVersion7():N}";
        var periodEnd = DateTimeOffset.UtcNow.AddDays(10);

        Fake.SeedSubscription(new StripeSubscriptionState(
            subscriptionId,
            Status: "past_due",
            CustomerId: "cus_test",
            CurrentPeriodEnd: periodEnd,
            CancelAtPeriodEnd: true,
            Metadata: new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));
        await DispatchAsync(new UpsertSubscriptionFromStripeCommand(subscriptionId));

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/subscriptions/me", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("planKey").GetString().ShouldBe("pro");
        data.GetProperty("status").GetString().ShouldBe("PastDue");
        data.GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeTrue();
        data.GetProperty("currentPeriodEnd").GetDateTimeOffset().ShouldBe(periodEnd, TimeSpan.FromSeconds(1));
    }

    private FakeStripeGateway Fake => (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();

    private async Task DispatchAsync(ICommand command)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(command);
    }
}
