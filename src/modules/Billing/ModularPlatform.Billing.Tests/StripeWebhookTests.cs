using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// Stripe webhook ingest (<c>POST /v1/billing/webhooks/stripe</c>, StripeWebhookEndpoint.cs).
/// Verifies signature handling + the atomic, idempotent ingest:
/// <list type="bullet">
/// <item>A correctly SIGNED event is accepted (200), persisted to <c>stripe_events</c> under the UNIQUE
/// <c>StripeEventId</c>, and the downstream ledger work is enqueued atomically via the Wolverine outbox
/// (a durable <c>ProcessStripeEventMessage</c> envelope).</item>
/// <item>Redelivery of the SAME signed event id is exactly-once at the ingest layer: exactly one
/// <c>stripe_events</c> row and exactly one enqueued envelope (the second POST hits the UNIQUE pre-check / race
/// guard and is a 200 no-op).</item>
/// <item>A BAD signature is rejected with 400 and NOTHING is persisted.</item>
/// </list>
///
/// IMPORTANT — what these tests can and cannot prove through the shared harness:
/// The webhook signature is verified with <c>EventUtility.ConstructEvent(rawBody, sig, WebhookSecret)</c>
/// (StripeWebhookEndpoint.cs:43-44). The integration host configures NO <c>Billing:Stripe:WebhookSecret</c>
/// (PlatformApiFactory.cs sets none; appsettings.json has none), so the secret is the empty string and a valid
/// signature is HMAC-SHA256 over <c>"{timestamp}.{json}"</c> with an EMPTY key — exactly what
/// <c>EventUtility.ComputeSignature</c> does. So the SIGNED ingest path IS reachable in-test.
/// One focused test overrides the host with a non-empty <c>Billing:Stripe:WebhookSecret</c> to prove an event
/// signed with the default empty key is rejected before persistence.
/// The DOWNSTREAM ledger top-up is NOT reachable: the worker command handler refetches the event with a LIVE
/// Stripe API call — <c>new EventService().GetAsync(...)</c> (ProcessStripeEventCommand.cs:29) — and no Stripe API
/// key is configured in the test host, so that call throws and the <c>CreditTopUpCommand</c> never runs. Therefore
/// these tests assert the ingest guarantees (200 / row / enqueued envelope / exactly-once) but deliberately do NOT
/// assert a Topup <c>credit_entries</c> row, a <c>CreditsToppedUp</c> envelope, or <c>ProcessedAt</c> being stamped
/// — all of which depend on that live external call. See scenariosSkipped notes in the run report.
/// </summary>
[Collection("Integration")]
public sealed class StripeWebhookTests(PlatformApiFactory fixture)
{
    private const string WebhookPath = "/v1/billing/webhooks/stripe";

    // The test host configures no Billing:Stripe:WebhookSecret -> StripeOptions.WebhookSecret defaults to "".
    private const string WebhookSecret = "";

    // Stripe.net 52 (api version "2026-05-27.dahlia") rejects an event whose api_version's second dotted segment
    // does not match "dahlia" (EventUtility.IsCompatibleApiVersion); ConstructEvent is called with
    // throwOnApiVersionMismatch defaulting to true, so the payload must carry a compatible api_version.
    private const string CompatibleApiVersion = "2026-05-27.dahlia";

    [Fact]
    public async Task Valid_signed_event_is_accepted_and_enqueues_durable_ledger_work()
    {
        var eventId = $"evt_{Guid.CreateVersion7():N}";
        var json = BuildEventJson(eventId, type: "checkout.session.completed");

        var response = await PostSignedAsync(json, SignNow(json));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The StripeEvent row is persisted under the UNIQUE StripeEventId. The row and the durable
        // ProcessStripeEventMessage envelope are written in ONE call (SaveChangesAndFlushMessagesAsync,
        // StripeWebhookEndpoint.cs) — so the row's existence is the reachable proof that the atomic ingest
        // committed (the outbox flush is part of the same transaction). We assert on stripe_events rather than
        // on Wolverine's internal envelope tables, which are an implementation detail (schema/table names + a
        // bytea body) and race with the retry->dead-letter lifecycle.
        var rows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}'");
        rows.ShouldBe(1);
    }

    [Fact]
    public async Task Redelivering_the_same_signed_event_is_exactly_once()
    {
        var eventId = $"evt_{Guid.CreateVersion7():N}";
        var json = BuildEventJson(eventId, type: "checkout.session.completed");

        // First delivery: accepted + persisted (one stripe_events row under the UNIQUE StripeEventId).
        var first = await PostSignedAsync(json, SignNow(json));
        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        await fixture.WaitForCountAsync(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}'", 1);

        // Stripe re-sends the SAME event id (a retry). Re-sign with a fresh timestamp so the signature is valid
        // again (the body / event id is identical — only the t= component differs).
        var second = await PostSignedAsync(json, SignNow(json));
        second.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Exactly-once at the ingest layer is DETERMINISTIC, not timing-dependent: the second POST returns only
        // after its synchronous UNIQUE pre-check (and the DB UNIQUE constraint as a backstop) has already rejected
        // the duplicate insert, so no fixed Task.Delay is needed to "wait for a duplicate that can never appear".
        var rows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}'");
        rows.ShouldBe(1);
    }

    [Fact]
    public async Task A_non_unique_persist_failure_is_not_acked_so_stripe_will_retry()
    {
        var eventId = $"evt_{Guid.CreateVersion7():N}";
        // A Type far longer than the stripe_events.Type column (varchar(128)) makes the INSERT fail with a
        // string-truncation error — a DbUpdateException that is NOT the UNIQUE race. The endpoint must NOT ACK it
        // (a 200 would tell Stripe "received" and the event would be lost forever); it must surface an error so
        // Stripe redelivers.
        var overLongType = "checkout.session." + new string('x', 200);
        var json = BuildEventJson(eventId, type: overLongType);

        var response = await PostSignedAsync(json, SignNow(json));

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError,
            "a transient/unexpected persist failure must not be ACKed — Stripe must redeliver");

        // Nothing was persisted (the failed SaveChanges rolled the transaction back BEFORE the 500 was returned),
        // so the redelivery can still succeed. Deterministic — no settle needed.
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Bad_signature_is_rejected_and_nothing_is_persisted()
    {
        var eventId = $"evt_{Guid.CreateVersion7():N}";
        var json = BuildEventJson(eventId, type: "checkout.session.completed");

        // A syntactically valid Stripe-Signature header whose v1 HMAC does not match the body -> StripeException
        // -> the endpoint returns 400 before persisting anything (StripeWebhookEndpoint.cs:46-50).
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var bogusSignature = $"t={t},v1={new string('0', 64)}";

        var response = await PostSignedAsync(json, bogusSignature);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Nothing persisted, DETERMINISTICALLY: the signature check rejects with 400 BEFORE db.StripeEvents.Add and
        // publishes no message, so there is no async path to wait out — assert immediately once the 400 is received.
        var rows = await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}'");
        rows.ShouldBe(0);
    }

    [Fact]
    public async Task Non_empty_webhook_secret_rejects_a_body_signed_with_the_default_empty_secret()
    {
        using var realSecretHost = fixture.CreateHost(
            ("Billing:Stripe:UseFakeGateway", "false"),
            ("Billing:Stripe:WebhookSecret", "whsec_live_test"));
        using var client = realSecretHost.CreateClient();

        var eventId = $"evt_{Guid.CreateVersion7():N}";
        var json = BuildEventJson(eventId, type: "checkout.session.completed");

        var response = await PostSignedAsync(client, json, SignNow(json));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}'")).ShouldBe(0);
    }

    private async Task<HttpResponseMessage> PostSignedAsync(string json, string stripeSignature)
    {
        return await PostSignedAsync(fixture.Client, json, stripeSignature);
    }

    private static async Task<HttpResponseMessage> PostSignedAsync(
        HttpClient client,
        string json,
        string stripeSignature)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, WebhookPath)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", stripeSignature);
        return await client.SendAsync(request);
    }

    /// <summary>Builds a Stripe-Signature header for the body at the current time, signed with the empty secret.</summary>
    private static string SignNow(string json)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = ComputeSignature(WebhookSecret, t.ToString(), json);
        return $"t={t},v1={signature}";
    }

    /// <summary>Mirrors Stripe.net's EventUtility.ComputeSignature: HMAC-SHA256 over "{t}.{payload}", hex lowercase.</summary>
    private static string ComputeSignature(string secret, string timestamp, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// A minimal but valid Stripe Event JSON. <c>api_version</c> must be compatible with Stripe.net's
    /// (second dotted segment "dahlia") or ConstructEvent throws on the version mismatch.
    /// </summary>
    private static string BuildEventJson(string eventId, string type) =>
        $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "{{CompatibleApiVersion}}",
          "type": "{{type}}",
          "data": {
            "object": {
              "id": "cs_test_{{Guid.CreateVersion7():N}}",
              "object": "checkout.session",
              "metadata": {}
            }
          }
        }
        """;
}
