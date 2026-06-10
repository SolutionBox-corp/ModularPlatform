using System.Net;
using System.Security.Cryptography;
using System.Text;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Billing.Tests;

/// <summary>
/// EV-3 — a message handler that keeps throwing is retried and then moved to Wolverine's durable
/// dead-letter store; it is NEVER silently marked Handled (the §9b ServiceLocationPolicy failure mode).
/// Arranged for real: a signed webhook for an event id the fake gateway does NOT know — the worker's
/// refetch throws on every attempt, the retry policy exhausts, the envelope dead-letters.
/// (EV-4 — kill-the-worker durability — is NOT coverable on TestServer: the harness runs Api+worker in
/// one process; it needs an out-of-process worker harness, tracked in docs/test-scenarios.md.)
/// </summary>
[Collection("Integration")]
public sealed class DeadLetterTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task EV3_throwing_handler_dead_letters_after_retries_instead_of_silently_handling()
    {
        // NOT seeded into FakeStripeGateway -> GetEventAsync throws on every retry.
        var eventId = $"evt_dead_{Guid.CreateVersion7():N}";

        var json = $$"""
        {
          "id": "{{eventId}}",
          "object": "event",
          "api_version": "2026-05-27.dahlia",
          "type": "payment_intent.succeeded",
          "data": { "object": { "object": "unused", "metadata": {} } }
        }
        """;
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(string.Empty));
        var signature = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{t}.{json}"))).ToLowerInvariant();
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/billing/webhooks/stripe")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Stripe-Signature", $"t={t},v1={signature}");

        (await fixture.Client.SendAsync(request)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Retries (100ms/500ms/3s cooldowns) then MoveToErrorQueue -> a durable dead-letter row appears.
        // The StripeException message carries the unknown event id, which makes the row attributable.
        await fixture.WaitForCountAsync(
            "SELECT count(*)::bigint FROM wolverine.wolverine_dead_letters " +
            $"WHERE exception_message LIKE '%{eventId}%'", 1, attempts: 150);

        // And the event row is NOT stamped processed — the failure is visible, not swallowed.
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM stripe_events WHERE \"StripeEventId\" = '{eventId}' AND \"ProcessedAt\" IS NULL"))
            .ShouldBe(1);
    }
}
