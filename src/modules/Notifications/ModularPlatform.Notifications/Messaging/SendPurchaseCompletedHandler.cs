using Microsoft.Extensions.Logging;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Features.Notifications.SendNotification;

namespace ModularPlatform.Notifications.Messaging;

/// <summary>
/// Reacts to Billing's <see cref="CreditPurchaseCompletedIntegrationEvent"/> by sending a
/// "purchase_completed" notification (in-app). Wolverine auto-discovers this <c>Handle</c>;
/// the inbox dedups. Reuses the <see cref="SendNotificationCommand"/> slice rather than
/// re-implementing delivery. A missing template is NON-FATAL — a deploy without the seed must
/// not poison the inbox.
/// </summary>
public sealed class SendPurchaseCompletedHandler(ILogger<SendPurchaseCompletedHandler> logger)
{
    public async Task Handle(
        CreditPurchaseCompletedIntegrationEvent message,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["locale"] = "en",
            ["creditAmount"] = message.CreditAmount.ToString(),
        };

        try
        {
            await dispatcher.Send(
                new SendNotificationCommand(
                    message.UserId,
                    "purchase_completed",
                    ["inapp"],
                    data,
                    IdempotencyKey: $"purchase-completed:{message.PurchaseId:N}"), ct);
        }
        catch (NotFoundException)
        {
            // No "purchase_completed" template configured for this deployment — skip rather than dead-letter.
            logger.LogWarning(
                "Purchase-completed notification skipped for {UserId}: no 'purchase_completed' template.",
                message.UserId);
        }
    }
}
