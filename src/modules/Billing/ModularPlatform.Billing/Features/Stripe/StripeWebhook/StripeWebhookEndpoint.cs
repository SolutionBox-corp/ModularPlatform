using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Entities;
using ModularPlatform.Billing.Messaging;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Security;
using Stripe;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Stripe.StripeWebhook;

/// <summary>
/// Thin Stripe webhook ingest. Verifies the signature against the RAW request body
/// (<see cref="EventUtility.ConstructEvent"/>), then ATOMICALLY persists the event id under a UNIQUE
/// constraint AND enqueues the ledger work via the Wolverine outbox: the <c>stripe_events</c> row and the
/// <see cref="ProcessStripeEventMessage"/> are committed in ONE transaction by
/// <c>SaveChangesAndFlushMessagesAsync</c> (no "saved but not enqueued" window). Returns 200 immediately;
/// does NO ledger mutation inline. Replays/duplicates are a no-op (already-persisted event id → UNIQUE).
/// </summary>
internal static class StripeWebhookEndpoint
{
    public static void MapStripeWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/webhooks/stripe", async (
                HttpRequest request,
                IDbContextOutbox<BillingDbContext> outbox,
                IOptions<StripeOptions> stripeOptions,
                IClock clock,
                CancellationToken ct) =>
            {
                using var reader = new StreamReader(request.Body);
                var rawBody = await reader.ReadToEndAsync(ct);
                var signature = request.Headers["Stripe-Signature"].ToString();

                Event stripeEvent;
                try
                {
                    stripeEvent = EventUtility.ConstructEvent(
                        rawBody, signature, stripeOptions.Value.WebhookSecret);
                }
                catch (StripeException)
                {
                    // Invalid signature — reject without persisting. Stripe will not retry a 400.
                    return Results.BadRequest();
                }

                var db = outbox.DbContext;

                var alreadySeen = await db.StripeEvents
                    .AnyAsync(e => e.StripeEventId == stripeEvent.Id, ct);
                if (alreadySeen)
                {
                    return Results.Ok();
                }

                db.StripeEvents.Add(new StripeEvent
                {
                    StripeEventId = stripeEvent.Id,
                    Type = stripeEvent.Type,
                    ReceivedAt = clock.UtcNow,
                    ProcessedAt = null,
                });

                await outbox.PublishAsync(new ProcessStripeEventMessage(stripeEvent.Id, stripeEvent.Type));

                try
                {
                    // Persist the event row AND flush the queued message in ONE transaction. Either both
                    // commit (row + enqueued work) or neither — no orphaned row, no lost message.
                    await outbox.SaveChangesAndFlushMessagesAsync();
                }
                catch (DbUpdateException)
                {
                    // Concurrent delivery of the same event id lost the UNIQUE race — already persisted and
                    // enqueued by the winner; idempotent, the queued message here is rolled back with the row.
                    return Results.Ok();
                }

                return Results.Ok();
            })
            .AllowAnonymous()
            .DisableRateLimiting() // Stripe delivers from many IPs and bursts on retry — never 429 a webhook.
            .WithTags("Billing")
            .WithName("StripeWebhook");
    }
}
