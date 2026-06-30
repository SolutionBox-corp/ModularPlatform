using Microsoft.Extensions.Logging;
using ModularPlatform.Billing.Contracts;
using ModularPlatform.Cqrs;
using ModularPlatform.Notifications.Contracts;

namespace ModularPlatform.Notifications.Messaging;

/// <summary>
/// Reacts to Billing's subscription dunning signal by sending a "subscription_past_due" in-app notification.
/// A missing template is non-fatal: billing state is still mirrored, and the next deployment/seed can fix UX.
/// </summary>
public sealed class SendSubscriptionPastDueHandler(ILogger<SendSubscriptionPastDueHandler> logger)
{
    public async Task Handle(
        SubscriptionPastDueIntegrationEvent message,
        IDispatcher dispatcher,
        CancellationToken ct)
    {
        var data = new Dictionary<string, string>
        {
            ["locale"] = "en",
            ["planKey"] = message.PlanKey,
            ["currentPeriodEnd"] = message.CurrentPeriodEnd?.ToString("O") ?? string.Empty,
        };

        try
        {
            await dispatcher.Send(
                new SendNotificationCommand(
                    message.UserId,
                    "subscription_past_due",
                    ["inapp"],
                    data,
                    IdempotencyKey: $"subscription-past-due:{message.UserId:N}:{message.PlanKey}"), ct);
        }
        catch (NotFoundException)
        {
            logger.LogWarning(
                "Subscription-past-due notification skipped for {UserId}: no 'subscription_past_due' template.",
                message.UserId);
        }
    }
}
