using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Billing.Features.Stripe.ReconcileStripe;
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

    private const string Password = "S3cure!pass";
    private FakeStripeGateway Fake => (FakeStripeGateway)fixture.Services.GetRequiredService<IStripeGateway>();

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

    private async Task DispatchReconcileAsync()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        await dispatcher.Send(new ReconcileStripeCommand());
    }
}
