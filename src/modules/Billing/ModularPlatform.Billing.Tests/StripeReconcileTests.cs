using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Features.Stripe.ReconcileStripe;
using ModularPlatform.Billing.Features.Subscriptions.UpsertSubscriptionFromStripe;
using ModularPlatform.Billing.Stripe;
using ModularPlatform.Cqrs;
using ModularPlatform.IntegrationTesting;
using Shouldly;
using Stripe;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// The Stripe reconcile sweep (<see cref="ReconcileStripeCommand"/>, cron-dispatched by the Jobs host):
/// a <c>stripe_events</c> row stuck unprocessed for over 30 minutes is re-queued through the outbox and —
/// thanks to the <c>IStripeGateway</c> fake — processed END TO END this time (ProcessedAt stamped, the
/// metadata top-up applied exactly once via the event-id idempotency key).
/// </summary>
[Collection("Integration")]
public sealed class StripeReconcileTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Stuck_stripe_event_is_requeued_and_processed_end_to_end_by_the_reconcile_sweep()
    {
        // A stuck ingest row: ProcessedAt NULL, received 45 minutes ago (beyond the 30-minute threshold).
        // Mirrors a webhook whose worker message dead-lettered (e.g. Stripe API outage at processing time).
        var eventId = $"evt_{Guid.CreateVersion7():N}";
        var userId = Guid.CreateVersion7();
        var stuckAt = DateTimeOffset.UtcNow.AddMinutes(-45);
        await fixture.ExecuteSqlAsync(
            $"""
             INSERT INTO stripe_events ("Id", "StripeEventId", "Type", "ReceivedAt", "ProcessedAt")
             VALUES ('{Guid.CreateVersion7()}', '{eventId}', 'payment_intent.succeeded', '{stuckAt:O}', NULL)
             """);

        // This time Stripe is reachable (the fake) and the event carries a metadata top-up.
        var fake = (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();
        fake.SeedEvent(new Event
        {
            Id = eventId,
            Type = "payment_intent.succeeded",
            Data = new EventData
            {
                Object = new PaymentIntent
                {
                    Id = $"pi_{Guid.CreateVersion7():N}",
                    Metadata = new Dictionary<string, string>
                    {
                        ["user_id"] = userId.ToString(),
                        ["credit_amount"] = "150",
                    },
                },
            },
        });

        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var result = await dispatcher.Send(new ReconcileStripeCommand());

        result.StuckEventsRequeued.ShouldBeGreaterThanOrEqualTo(1);

        // The re-queued message drains end to end: ProcessedAt stamped AND the ledger top-up applied once.
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}' AND \"ProcessedAt\" IS NOT NULL", 1);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = '{eventId}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Stuck_PAID_purchase_whose_confirmation_dead_lettered_is_regranted()
    {
        // A purchase whose CreditPurchaseConfirmed dead-lettered: the saga is stuck Abandoned (the grant never
        // landed) but the Stripe checkout session is actually PAID. The reconcile must re-grant — the customer
        // paid and must get their credits. Idempotency key purchase:{id} makes the re-grant safe under any race.
        var (userId, _) = await fixture.RegisterAndLoginAsync($"regrant-{Guid.CreateVersion7():N}@test.io", Password);
        var (purchaseId, sessionId) = await SeedStuckSagaAsync(userId, creditAmount: 500, status: "Abandoned");

        Fake.SeedCheckoutSessionStatus(sessionId, "paid");

        await DispatchReconcileAsync();

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'", 1);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM credit_purchase_sagas WHERE \"Id\" = '{purchaseId}' AND \"Status\" = 'Completed'", 1);
    }

    [Fact]
    public async Task Stuck_UNPAID_purchase_is_NOT_regranted()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"noregrant-{Guid.CreateVersion7():N}@test.io", Password);
        var (purchaseId, sessionId) = await SeedStuckSagaAsync(userId, creditAmount: 500, status: "Abandoned");

        Fake.SeedCheckoutSessionStatus(sessionId, "unpaid"); // funds NOT captured

        await DispatchReconcileAsync();

        // Negative settle: a regression that grants on "unpaid" would land within this window.
        await Task.Delay(750);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM credit_entries WHERE \"IdempotencyKey\" = 'purchase:{purchaseId}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Subscription_drift_is_corrected_from_live_stripe_state()
    {
        var (userId, _) = await fixture.RegisterAndLoginAsync($"drift-{Guid.CreateVersion7():N}@test.io", Password);
        var subscriptionId = await MirrorSubscriptionAsync(userId, "active", cancelAtPeriodEnd: false);
        var livePeriodEnd = DateTimeOffset.UtcNow.AddDays(7);

        Fake.SeedSubscription(new StripeSubscriptionState(
            subscriptionId,
            Status: "past_due",
            CustomerId: "cus_drift",
            CurrentPeriodEnd: livePeriodEnd,
            CancelAtPeriodEnd: true,
            Metadata: new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));

        var result = await DispatchReconcileAsync();

        result.SubscriptionDriftsFixed.ShouldBeGreaterThanOrEqualTo(1);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM subscriptions WHERE \"StripeSubscriptionId\" = '{subscriptionId}' AND \"Status\" = 'PastDue' AND \"CancelAtPeriodEnd\" = true",
            1);
    }

    [Fact]
    public async Task Provider_errors_are_isolated_per_subscription()
    {
        var (brokenUserId, _) = await fixture.RegisterAndLoginAsync($"drift-fail-{Guid.CreateVersion7():N}@test.io", Password);
        var brokenSubscriptionId = await MirrorSubscriptionAsync(brokenUserId, "active", cancelAtPeriodEnd: false);
        Fake.SeedSubscription(new StripeSubscriptionState(
            brokenSubscriptionId,
            Status: "past_due",
            CustomerId: "cus_down",
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddDays(3),
            CancelAtPeriodEnd: true,
            Metadata: new Dictionary<string, string> { ["user_id"] = brokenUserId.ToString(), ["plan_key"] = "pro" }));

        var (fixedUserId, _) = await fixture.RegisterAndLoginAsync($"drift-fix-{Guid.CreateVersion7():N}@test.io", Password);
        var fixedSubscriptionId = await MirrorSubscriptionAsync(fixedUserId, "active", cancelAtPeriodEnd: false);
        Fake.SeedSubscription(new StripeSubscriptionState(
            fixedSubscriptionId,
            Status: "past_due",
            CustomerId: "cus_fixed",
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddDays(4),
            CancelAtPeriodEnd: true,
            Metadata: new Dictionary<string, string> { ["user_id"] = fixedUserId.ToString(), ["plan_key"] = "pro" }));

        Fake.FailNextSubscriptionLookup();

        await DispatchReconcileAsync();

        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM subscriptions WHERE \"StripeSubscriptionId\" = '{fixedSubscriptionId}' AND \"Status\" = 'PastDue'",
            1);
    }

    [Fact]
    public async Task Stuck_purchase_reconcile_pass_is_capped_per_run()
    {
        var before = Fake.CheckoutSessionStatusLookupCount;
        var purchaseIds = new List<Guid>();

        try
        {
            for (var i = 0; i < 205; i++)
            {
                var (purchaseId, _) = await SeedStuckSagaAsync(Guid.CreateVersion7(), creditAmount: 1, status: "Abandoned");
                purchaseIds.Add(purchaseId);
            }

            var result = await DispatchReconcileAsync();

            var checkedSessions = Fake.CheckoutSessionStatusLookupCount - before;
            checkedSessions.ShouldBe(200);
            result.StuckPurchaseCapReached.ShouldBeTrue();
        }
        finally
        {
            if (purchaseIds.Count > 0)
            {
                await fixture.ExecuteSqlAsync(
                    $"""DELETE FROM credit_purchase_sagas WHERE "Id" IN ({string.Join(",", purchaseIds.Select(id => $"'{id}'"))})""");
            }
        }
    }

    [Fact]
    public async Task Stuck_event_reconcile_pass_reports_when_the_per_run_cap_is_reached()
    {
        var prefix = $"evt_cap_{Guid.CreateVersion7():N}";
        var receivedAt = DateTimeOffset.UtcNow.AddMinutes(-45);

        try
        {
            for (var i = 0; i < 200; i++)
            {
                await fixture.ExecuteSqlAsync(
                    $"""
                     INSERT INTO stripe_events ("Id", "StripeEventId", "Type", "ReceivedAt", "ProcessedAt")
                     VALUES ('{Guid.CreateVersion7()}', '{prefix}_{i}', 'customer.subscription.updated', '{receivedAt:O}', NULL)
                     """);
            }

            var result = await DispatchReconcileAsync();

            result.StuckEventsRequeued.ShouldBe(200);
            result.StuckEventCapReached.ShouldBeTrue();
        }
        finally
        {
            await fixture.ExecuteSqlAsync($"""DELETE FROM stripe_events WHERE "StripeEventId" LIKE '{prefix}_%'""");
        }
    }

    [Fact]
    public async Task Subscription_reconcile_pass_reports_when_the_per_run_cap_is_reached()
    {
        var prefix = $"sub_cap_{Guid.CreateVersion7():N}";
        var now = DateTimeOffset.UtcNow;

        var subscriptionRows = string.Join(",",
            Enumerable.Range(1, 500).Select(i =>
                $"('{Guid.CreateVersion7()}','{Guid.CreateVersion7()}','pro','{prefix}_{i}','cus_cap_{i}'," +
                $"'Active','{now.AddDays(30):O}',false,'{now.AddMinutes(-5):O}','{now.AddMinutes(-5):O}')"));

        try
        {
            await fixture.ExecuteSqlAsync($"""
                INSERT INTO subscriptions
                  ("Id", "UserId", "PlanKey", "StripeSubscriptionId", "StripeCustomerId", "Status",
                   "CurrentPeriodEnd", "CancelAtPeriodEnd", "CreatedAt", "UpdatedAt")
                VALUES {subscriptionRows};
                """);

            var result = await DispatchReconcileAsync();

            result.SubscriptionCapReached.ShouldBeTrue();
        }
        finally
        {
            await fixture.ExecuteSqlAsync($"""DELETE FROM subscriptions WHERE "StripeSubscriptionId" LIKE '{prefix}_%'""");
        }
    }

    private const string Password = "S3cure!pass";
    private FakeStripeGateway Fake => (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();

    private async Task<string> MirrorSubscriptionAsync(Guid userId, string stripeStatus, bool cancelAtPeriodEnd)
    {
        var subscriptionId = $"sub_{Guid.CreateVersion7():N}";
        Fake.SeedSubscription(new StripeSubscriptionState(
            subscriptionId,
            Status: stripeStatus,
            CustomerId: $"cus_{Guid.CreateVersion7():N}",
            CurrentPeriodEnd: DateTimeOffset.UtcNow.AddMonths(1),
            CancelAtPeriodEnd: cancelAtPeriodEnd,
            Metadata: new Dictionary<string, string> { ["user_id"] = userId.ToString(), ["plan_key"] = "pro" }));
        await DispatchAsync(new UpsertSubscriptionFromStripeCommand(subscriptionId));
        return subscriptionId;
    }

    private async Task<(Guid PurchaseId, string SessionId)> SeedStuckSagaAsync(Guid userId, long creditAmount, string status)
    {
        var purchaseId = Guid.CreateVersion7();
        var sessionId = $"cs_test_{Guid.CreateVersion7():N}";
        await fixture.ExecuteSqlAsync(
            $"""
             INSERT INTO credit_purchase_sagas
               ("Id","UserId","PackageId","CheckoutSessionId","CreditAmount","BucketExpiryDays","Status","StartedAt","Version")
             VALUES ('{purchaseId}','{userId}','{Guid.CreateVersion7()}','{sessionId}',{creditAmount},NULL,'{status}',
                     '{DateTimeOffset.UtcNow.AddMinutes(-45):O}',1)
             """);
        return (purchaseId, sessionId);
    }

    private async Task<ReconcileStripeResponse> DispatchReconcileAsync()
    {
        return await DispatchAsync(new ReconcileStripeCommand());
    }

    private async Task<TResponse> DispatchAsync<TResponse>(ICommand<TResponse> command)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        return await dispatcher.Send(command);
    }
}
