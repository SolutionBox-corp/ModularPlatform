using ModularPlatform.Billing.Features.Credits.EnsureCreditAccount;
using ModularPlatform.Cqrs;
using ModularPlatform.Identity.Contracts;

namespace ModularPlatform.Billing.Messaging;

/// <summary>
/// Consumes Identity's <see cref="UserRegisteredIntegrationEvent"/> (the ONLY way Billing learns about new
/// users — never by referencing Identity's Core) and provisions a zero-balance credit account. This is a thin
/// PUBLIC shell (Wolverine-discovered, runs in the Worker, inbox-deduped) that dispatches the internal command —
/// it takes only public types so the module's Core stays internal.
/// </summary>
public sealed class UserRegisteredHandler
{
    public Task Handle(UserRegisteredIntegrationEvent message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new EnsureCreditAccountCommand(message.UserId), ct);
}
