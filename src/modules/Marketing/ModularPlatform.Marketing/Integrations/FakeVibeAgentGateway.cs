using System.Text.Json;

namespace ModularPlatform.Marketing.Integrations;

/// <summary>
/// Deterministic in-memory vibe-agent gateway for dev/tests (registered under <c>Marketing:UseFakeGateways=true</c>).
/// Echoes the last user message and emits a single mock tool-call trace so the chat pipeline (persist user turn →
/// durable worker → assistant turn + realtime) is exercisable without a Claude API key.
/// </summary>
internal sealed class FakeVibeAgentGateway : IVibeAgentGateway
{
    public Task<VibeTurnResult> RunTurnAsync(
        Guid userId, IReadOnlyList<VibeTurnInput> history, CancellationToken ct)
    {
        var lastUser = history.LastOrDefault(t => t.Role == "user")?.Content ?? "(no question)";

        var content =
            $"Based on your marketing data, here's a quick take on \"{lastUser}\": organic search is your strongest " +
            "channel and lead conversion is steady. Next step: double down on your top-performing keywords.";

        var toolCalls = JsonSerializer.Serialize(new[]
        {
            new
            {
                tool = "list_recent_pulls",
                arguments = new { limit = 10 },
                result = Array.Empty<object>(),
            },
        });

        return Task.FromResult(new VibeTurnResult(content, toolCalls));
    }
}
