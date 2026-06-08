using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ModularPlatform.Abstractions;
using ModularPlatform.Billing.Features.Credits.CreditTopUp;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Cqrs;
using Stripe;

namespace ModularPlatform.Billing.Features.Credits.ProcessStripeEvent;

/// <summary>
/// IDEMPOTENT ledger top-up from a Stripe event: refetch the event, read user + amount from object metadata
/// (reconcile against state, never assume webhook order), dispatch the idempotent <see cref="CreditTopUpCommand"/>
/// (idempotency key = Stripe event id), stamp the StripeEvent processed. Inbox + ledger idempotency = exactly-once.
/// </summary>
internal sealed record ProcessStripeEventCommand(string StripeEventId) : ICommand;

internal sealed class ProcessStripeEventHandler(BillingDbContext db, IDispatcher dispatcher, IClock clock)
    : ICommandHandler<ProcessStripeEventCommand, Unit>
{
    public async Task<Unit> Handle(ProcessStripeEventCommand command, CancellationToken ct)
    {
        var record = await db.StripeEvents.FirstOrDefaultAsync(e => e.StripeEventId == command.StripeEventId, ct);
        if (record is null || record.ProcessedAt is not null)
        {
            return Unit.Value;
        }

        var stripeEvent = await new EventService().GetAsync(command.StripeEventId, cancellationToken: ct);

        if (TryExtractTopUp(stripeEvent, out var userId, out var amount, out var bucketExpiryDays))
        {
            await dispatcher.Send(new CreditTopUpCommand(userId, amount, bucketExpiryDays, command.StripeEventId), ct);
        }

        record.ProcessedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return Unit.Value;
    }

    private static bool TryExtractTopUp(Event stripeEvent, out Guid userId, out long amount, out int? bucketExpiryDays)
    {
        userId = Guid.Empty;
        amount = 0;
        bucketExpiryDays = null;

        var metadata = (stripeEvent.Data?.Object as IHasMetadata)?.Metadata;
        if (metadata is null
            || !metadata.TryGetValue("user_id", out var rawUserId)
            || !Guid.TryParse(rawUserId, out userId)
            || !metadata.TryGetValue("credit_amount", out var rawAmount)
            || !long.TryParse(rawAmount, NumberStyles.Integer, CultureInfo.InvariantCulture, out amount)
            || amount <= 0)
        {
            return false;
        }

        if (metadata.TryGetValue("bucket_expiry_days", out var rawExpiry)
            && int.TryParse(rawExpiry, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expiry)
            && expiry > 0)
        {
            bucketExpiryDays = expiry;
        }

        return true;
    }
}
