using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// UC43: the CRM only asks the platform for a hosted billing portal URL. The customer id and return URL are
/// server-owned, so the CRM never creates a provider session directly and never sends customer ids in the request.
/// </summary>
[Collection("Integration")]
public sealed class BillingPortalTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Billing_portal_returns_422_when_user_has_no_provider_customer()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync(
            $"uc43-empty-{Guid.CreateVersion7():N}@example.test", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/portal", token));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.no_billing_account");
    }

    [Fact]
    public async Task Billing_portal_returns_provider_url_for_existing_customer()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"uc43-ok-{Guid.CreateVersion7():N}@example.test", Password);
        await MirrorSubscriptionAsync(userId, "cus_portal_ok");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/portal", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        var url = data.GetProperty("url").GetString();
        url.ShouldNotBeNull();
        url.ShouldStartWith("https://billing.stripe.test/portal/cus_portal_ok");
        url.ShouldContain(Uri.EscapeDataString("https://app.test/billing/success"));
    }

    [Fact]
    public async Task Billing_portal_translates_provider_failure_to_domain_error()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"uc43-provider-{Guid.CreateVersion7():N}@example.test", Password);
        await MirrorSubscriptionAsync(userId, "cus_portal_down");
        Fake.FailNextBillingPortalSession();

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/portal", token));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.portal.provider_failed");
    }

    [Fact]
    public async Task Billing_portal_rejects_invalid_return_url_before_provider_call()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"uc43-return-{Guid.CreateVersion7():N}@example.test", Password);
        await MirrorSubscriptionAsync(userId, "cus_portal_return");

        using var host = fixture.CreateHost(
            ("Billing:Stripe:SuccessUrl", "not-a-url"));
        using var client = host.CreateClient();

        var response = await client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/portal", token));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.portal.invalid_return_url");
    }

    private async Task MirrorSubscriptionAsync(Guid userId, string customerId)
    {
        var subscriptionId = $"sub_{Guid.CreateVersion7():N}";
        Fake.SeedSubscription(new StripeSubscriptionState(
            subscriptionId,
            Status: "active",
            CustomerId: customerId,
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddMonths(1),
            CancelAtPeriodEnd: false,
            Metadata: new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));
        await DispatchAsync(new UpsertSubscriptionFromStripeCommand(subscriptionId));
    }

    private FakeStripeGateway Fake => (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();

    private async Task DispatchAsync(ICommand command)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(command);
    }
}
