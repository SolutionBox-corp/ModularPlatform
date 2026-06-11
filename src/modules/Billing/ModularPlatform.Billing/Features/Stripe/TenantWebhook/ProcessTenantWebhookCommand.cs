using System.Globalization;
using ModularPlatform.Billing.Persistence;
using ModularPlatform.Billing.Sagas;
using ModularPlatform.Cqrs;
using ModularPlatform.Payments;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Billing.Features.Stripe.TenantWebhook;

/// <summary>
/// Processes a per-tenant payment webhook (<c>/billing/webhooks/{provider}/{tenantId}</c>). Resolves the TENANT's own
/// gateway and asks it for the AUTHORITATIVE state (Stripe: verify signature; GoPay/Fake: re-fetch — never trust the
/// payload). On a PAID package checkout it outboxes <see cref="CreditPurchaseConfirmed"/>, which the existing saga
/// grants idempotently (<c>purchase:{id}</c>) — the grant logic is UNCHANGED, only the gateway/transport differs.
/// </summary>
public sealed record ProcessTenantWebhookCommand(
    Guid TenantId,
    string RawBody,
    string? SignatureHeader,
    IReadOnlyDictionary<string, string> Query) : ICommand;

internal sealed class ProcessTenantWebhookHandler(
    IPaymentGatewayResolver resolver,
    IDbContextOutbox<BillingDbContext> outbox)
    : ICommandHandler<ProcessTenantWebhookCommand, Unit>
{
    public async Task<Unit> Handle(ProcessTenantWebhookCommand command, CancellationToken ct)
    {
        PaymentSnapshot snapshot;
        try
        {
            var gateway = await resolver.ResolveAsync(command.TenantId, PaymentPlane.Tenant, ct);
            snapshot = await gateway.VerifyNotificationAsync(
                new NotificationContext(command.RawBody, command.SignatureHeader, command.Query), ct);
        }
        catch (PaymentGatewayUnavailableException)
        {
            // Unknown/inactive tenant gateway — acknowledge (200) and ignore; never act on an unverifiable notification.
            return Unit.Value;
        }

        if (snapshot.State != PaymentState.Paid
            || !snapshot.Metadata.TryGetValue("purchase_type", out var purchaseType) || purchaseType != "package"
            || !snapshot.Metadata.TryGetValue("purchase_id", out var purchaseIdRaw)
            || !Guid.TryParse(purchaseIdRaw, out var purchaseId)
            || !snapshot.Metadata.TryGetValue("user_id", out var userIdRaw) || !Guid.TryParse(userIdRaw, out var userId)
            || !snapshot.Metadata.TryGetValue("credit_amount", out var amountRaw)
            || !long.TryParse(amountRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var creditAmount))
        {
            return Unit.Value;
        }

        int? bucketExpiry = snapshot.Metadata.TryGetValue("bucket_expiry_days", out var be)
            && int.TryParse(be, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
            ? days
            : null;

        // The saga grants exactly once via the idempotency key purchase:{id} — replays / duplicate webhooks are safe.
        await outbox.PublishAsync(new CreditPurchaseConfirmed(
            Id: purchaseId,
            UserId: userId,
            CreditAmount: creditAmount,
            BucketExpiryDays: bucketExpiry,
            StripeEventId: snapshot.ProviderPaymentId));
        await outbox.SaveChangesAndFlushMessagesAsync();

        return Unit.Value;
    }
}
