using ModularPlatform.Billing.Features.Credits.ProcessStripeEvent;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Billing.Messaging;

/// <summary>
/// Worker-side PUBLIC shell for <see cref="ProcessStripeEventMessage"/> (enqueued by the webhook). Wolverine
/// auto-discovers it and the inbox dedups redelivery; it dispatches the internal command that runs the idempotent
/// ledger top-up. Thin shell with only public types so the module's Core stays internal.
/// </summary>
public sealed class ProcessStripeEventHandler
{
    public Task Handle(ProcessStripeEventMessage message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new ProcessStripeEventCommand(message.StripeEventId), ct);
}
