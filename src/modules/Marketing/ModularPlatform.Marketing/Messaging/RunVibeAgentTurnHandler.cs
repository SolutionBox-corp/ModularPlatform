using ModularPlatform.Cqrs;
using ModularPlatform.Marketing.Features.Vibe.ProcessVibeTurn;

namespace ModularPlatform.Marketing.Messaging;

/// <summary>
/// Durable Worker shell for one vibe-chat agent turn: a thin PUBLIC type (Wolverine-discovered, inbox-deduped) that
/// dispatches the internal <see cref="ProcessVibeTurnCommand"/> — it takes only public types so the module's Core stays
/// internal. All the work (load history, call the agent gateway, persist the assistant turn, realtime push) lives in
/// the command handler. Mirrors <see cref="RunDataPullHandler"/>.
/// </summary>
public sealed class RunVibeAgentTurnHandler
{
    public Task Handle(RunVibeAgentTurn message, IDispatcher dispatcher, CancellationToken ct) =>
        dispatcher.Send(new ProcessVibeTurnCommand(message.ConversationId, message.UserId), ct);
}
