using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Pulls.ExecutePull;

namespace ModularPlatform.Marketing.Messaging;

/// <summary>
/// Durable Worker shell for a marketing data pull: a thin PUBLIC type (Wolverine-discovered, inbox-deduped) that
/// dispatches the internal <see cref="ExecutePullCommand"/> — it takes only public types so the module's Core stays
/// internal. All the work (gateway call, snapshot persistence, terminal transition) lives in the command handler.
/// </summary>
public sealed class RunDataPullHandler
{
    public Task Handle(RunDataPull message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new ExecutePullCommand(message.DataPullId), ct);
}
