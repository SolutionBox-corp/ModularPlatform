namespace ModularPlatform.Marketing.Integrations;

/// <summary>One turn of the vibe-chat history fed to the agent: a role (<c>user</c> | <c>assistant</c> | <c>system</c>) + text.</summary>
internal sealed record VibeTurnInput(string Role, string Content);

/// <summary>
/// The agent's reply for a turn: the assistant text plus the verbatim tool-call trace (JSON array of
/// <c>{ tool, arguments, result }</c>) so the UI can replay the reasoning. <see cref="ToolCallsJson"/> is null for a
/// plain text answer (no tools used).
/// </summary>
internal sealed record VibeTurnResult(string Content, string? ToolCallsJson);

/// <summary>
/// Anti-corruption port to the agentic "vibe marketing" chat brain (Claude). The real implementation wraps
/// <c>IChatClient</c> with a bounded read-only tool-use loop over the CALLER's own marketing data; the deterministic
/// <c>FakeVibeAgentGateway</c> replaces it under <c>Marketing:UseFakeGateways</c> so the chat pipeline is testable
/// without an API key. The <paramref name="userId"/> scopes every tool to the caller's rows.
/// </summary>
internal interface IVibeAgentGateway
{
    Task<VibeTurnResult> RunTurnAsync(Guid userId, IReadOnlyList<VibeTurnInput> history, CancellationToken ct);
}
