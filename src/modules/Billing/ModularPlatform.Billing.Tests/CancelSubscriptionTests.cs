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
/// UC42: cancellation calls the provider first, then eagerly mirrors the user's intent locally while the authoritative
/// terminal state still comes from provider webhooks/reconciliation.
/// </summary>
[Collection("Integration")]
public sealed class CancelSubscriptionTests(PlatformApiFactory fixture)
{
    private const string Password = "S3cure!pass";

    [Fact]
    public async Task Cancel_subscription_returns_404_when_user_has_no_active_subscription()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"uc42-empty-{Guid.CreateVersion7():N}@example.test", Password);

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/cancel", token));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.subscription.not_found");
    }

    [Fact]
    public async Task Cancel_subscription_is_idempotent_while_cancel_at_period_end_is_pending()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"uc42-idempotent-{Guid.CreateVersion7():N}@example.test", Password);
        var subscriptionId = await MirrorActiveSubscriptionAsync(userId, cancelAtPeriodEnd: false);

        var first = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/cancel", token));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        var firstData = await PlatformApiFactory.ReadData(first);
        firstData.GetProperty("status").GetString().ShouldBe("Active");
        firstData.GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeTrue();

        var second = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/cancel", token));
        second.StatusCode.ShouldBe(HttpStatusCode.OK);
        var secondData = await PlatformApiFactory.ReadData(second);
        secondData.GetProperty("status").GetString().ShouldBe("Active");
        secondData.GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeTrue();

        var localFlag = await fixture.ScalarAsync<bool>(
            $"SELECT \"CancelAtPeriodEnd\" FROM subscriptions WHERE \"StripeSubscriptionId\" = '{subscriptionId}'");
        localFlag.ShouldBeTrue();
    }

    [Fact]
    public async Task Cancel_subscription_can_immediately_mark_the_local_mirror_canceled()
    {
        using var host = fixture.CreateHost(("Billing:Subscriptions:CancelAtPeriodEnd", "false"));
        using var client = host.CreateClient();
        var (userId, token) = await RegisterAndLoginAsync(
            client, $"uc42-immediate-{Guid.CreateVersion7():N}@example.test");
        var subscriptionId = await MirrorActiveSubscriptionAsync(
            host.Services, userId, cancelAtPeriodEnd: false);

        var response = await client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/cancel", token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(response);
        data.GetProperty("status").GetString().ShouldBe("Canceled");
        data.GetProperty("cancelAtPeriodEnd").GetBoolean().ShouldBeFalse();

        var localState = await fixture.ScalarAsync<string>(
            $"SELECT \"Status\" || ':' || \"CancelAtPeriodEnd\"::text FROM subscriptions WHERE \"StripeSubscriptionId\" = '{subscriptionId}'");
        localState.ShouldBe("Canceled:false");

        var me = await client.SendAsync(fixture.Authed(
            HttpMethod.Get, "/v1/billing/subscriptions/me", token));
        me.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancel_subscription_translates_provider_failure_to_domain_error()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"uc42-provider-{Guid.CreateVersion7():N}@example.test", Password);
        var missingProviderId = $"sub_missing_{Guid.CreateVersion7():N}";
        await fixture.ExecuteSqlAsync(
            "INSERT INTO subscriptions " +
            "(\"Id\", \"UserId\", \"PlanKey\", \"StripeSubscriptionId\", \"StripeCustomerId\", \"Status\", \"CurrentPeriodEnd\", \"CancelAtPeriodEnd\", \"CreatedAt\") " +
            $"VALUES ('{Guid.CreateVersion7()}', '{userId}', 'pro', '{missingProviderId}', 'cus_missing', 'Active', now() + interval '1 month', false, now())");

        var response = await fixture.Client.SendAsync(fixture.Authed(
            HttpMethod.Post, "/v1/billing/subscriptions/cancel", token));

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.Content.ReadAsStringAsync()).ShouldContain("billing.subscription.provider_failed");
    }

    private async Task<string> MirrorActiveSubscriptionAsync(Guid userId, bool cancelAtPeriodEnd)
    {
        return await MirrorActiveSubscriptionAsync(fixture.Services, userId, cancelAtPeriodEnd);
    }

    private static async Task<string> MirrorActiveSubscriptionAsync(
        IServiceProvider services, Guid userId, bool cancelAtPeriodEnd)
    {
        var subscriptionId = $"sub_{Guid.CreateVersion7():N}";
        var fake = (FakeStripeGateway)services.GetRequiredService<IStripeGateway>();
        fake.SeedSubscription(new StripeSubscriptionState(
            subscriptionId,
            Status: "active",
            CustomerId: "cus_test",
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddMonths(1),
            CancelAtPeriodEnd: cancelAtPeriodEnd,
            Metadata: new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));
        await DispatchAsync(services, new UpsertSubscriptionFromStripeCommand(subscriptionId));
        return subscriptionId;
    }

    private async Task DispatchAsync(ICommand command)
    {
        await DispatchAsync(fixture.Services, command);
    }

    private static async Task DispatchAsync(IServiceProvider services, ICommand command)
    {
        await using var scope = services.CreateAsyncScope();
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
