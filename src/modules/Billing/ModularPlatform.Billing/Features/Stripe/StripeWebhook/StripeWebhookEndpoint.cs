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
using Wolverine;

namespace ModularPlatform.Billing.Features.Stripe.StripeWebhook;

/// <summary>
/// Thin Stripe webhook ingest. Verifies the signature against the RAW request body
/// (<see cref="EventUtility.ConstructEvent"/>), persists the event id under a UNIQUE constraint in ONE
/// transaction (webhook idempotency ledger), returns 200 immediately, then enqueues the ledger work to the
/// Worker. Does NO ledger mutation inline. Replays/duplicates are a no-op (already-persisted event id).
/// </summary>
internal static class StripeWebhookEndpoint
{
    public static void MapStripeWebhook(this IEndpointRouteBuilder app)
    {
        app.MapPost("/billing/webhooks/stripe", async (
                HttpRequest request,
                BillingDbContext db,
                IMessageBus bus,
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

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateException)
                {
                    // Concurrent delivery of the same event id lost the race — already persisted, idempotent.
                    return Results.Ok();
                }

                await bus.PublishAsync(new ProcessStripeEventMessage(stripeEvent.Id, stripeEvent.Type));

                return Results.Ok();
            })
            .AllowAnonymous()
            .WithTags("Billing")
            .WithName("StripeWebhook");
    }
}
