using System.ComponentModel;
using System.Text.Json;
using Anthropic.SDK;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModularPlatform.Marketing.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Marketing.Integrations;

/// <summary>
/// Real "vibe marketing" agent: drives Claude (via <c>IChatClient</c>) through a BOUNDED read-only tool-use loop over
/// the CALLER's own marketing data. Tools are user-scoped <c>AIFunction</c>s resolved against the read context
/// (<c>WHERE UserId == userId</c> + RLS), so the agent can only ever read the caller's rows. The <c>IChatClient</c>'s
/// <c>FunctionInvokingChatClient</c> middleware runs the call→tool→call loop and caps it at
/// <c>MaximumIterationsPerRequest</c> (~6). The full tool-call trace is projected to <see cref="VibeTurnResult.ToolCallsJson"/>.
/// Wired only when <c>Marketing:UseFakeGateways=false</c>, so a missing key surfaces at call time, not at boot in tests.
/// </summary>
internal sealed class ClaudeVibeAgentGateway(
    IOptions<MarketingClaudeOptions> options,
    IReadDbContextFactory<MarketingDbContext> readDb)
    : IVibeAgentGateway
{
    private const int MaxToolIterations = 6;

    private const string SystemPrompt =
        "You are a senior marketing analyst embedded in the user's analytics workspace. You answer questions about " +
        "the user's own GA4 (Google Analytics) and Google Search Console data. Use the provided read-only tools to " +
        "fetch the user's recent data pulls, metric snapshots and saved AI analyses before answering — never invent " +
        "numbers. Be concise, lead with the headline insight, and give one concrete next action when relevant.";

    private readonly MarketingClaudeOptions _options = options.Value;

    public async Task<VibeTurnResult> RunTurnAsync(
        Guid userId, IReadOnlyList<VibeTurnInput> history, CancellationToken ct)
    {
        using var client = BuildClient(userId, history, out var messages, out var chatOptions);

        var response = await client.GetResponseAsync(messages, chatOptions, ct);

        var toolCalls = ExtractToolCalls(response);
        var toolCallsJson = toolCalls.Count > 0 ? JsonSerializer.Serialize(toolCalls) : null;

        return new VibeTurnResult((response.Text ?? string.Empty).Trim(), toolCallsJson);
    }

    public async IAsyncEnumerable<string> RunTurnStreamingAsync(
        Guid userId,
        IReadOnlyList<VibeTurnInput> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        using var client = BuildClient(userId, history, out var messages, out var chatOptions);

        // Same client + tools + UseFunctionInvocation loop, but streamed: the function-invoking middleware still runs
        // the call→tool→call loop transparently; we yield only the assistant TEXT deltas (ChatResponseUpdate.Text
        // skips reasoning/tool content), so the UI shows tokens as they arrive.
        await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, ct))
        {
            var delta = update.Text;
            if (!string.IsNullOrEmpty(delta))
            {
                yield return delta;
            }
        }
    }

    /// <summary>
    /// Builds the configured tool-using <see cref="IChatClient"/> + the seeded message list + <see cref="ChatOptions"/>
    /// shared by the buffered and streaming turn paths (same client, tools and <c>UseFunctionInvocation</c> loop).
    /// </summary>
    private IChatClient BuildClient(
        Guid userId,
        IReadOnlyList<VibeTurnInput> history,
        out List<ChatMessage> messages,
        out ChatOptions chatOptions)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Marketing:Claude:ApiKey is not configured.");
        }

        var tools = new VibeAgentTools(readDb, userId);

        IChatClient client = new ChatClientBuilder(new AnthropicClient(_options.ApiKey).Messages)
            .ConfigureOptions(o => o.ModelId ??= _options.Model)
            .UseFunctionInvocation(configure: f => f.MaximumIterationsPerRequest = MaxToolIterations)
            .Build();

        messages = [new ChatMessage(ChatRole.System, SystemPrompt)];
        foreach (var turn in history)
        {
            messages.Add(new ChatMessage(MapRole(turn.Role), turn.Content));
        }

        chatOptions = new ChatOptions
        {
            Tools =
            [
                AIFunctionFactory.Create(tools.ListRecentPullsAsync, "list_recent_pulls"),
                AIFunctionFactory.Create(tools.ListSnapshotsAsync, "list_snapshots"),
                AIFunctionFactory.Create(tools.ListAnalysesAsync, "list_analyses"),
                AIFunctionFactory.Create(tools.GetAnalysisAsync, "get_analysis"),
            ],
        };

        return client;
    }

    private static ChatRole MapRole(string role) => role switch
    {
        "assistant" => ChatRole.Assistant,
        "system" => ChatRole.System,
        _ => ChatRole.User,
    };

    /// <summary>
    /// Projects the function-call / function-result content the function-invoking middleware appended to the response
    /// into a flat <c>[{ tool, arguments, result }]</c> trace. Results are matched to their call by <c>CallId</c>.
    /// </summary>
    private static List<object> ExtractToolCalls(ChatResponse response)
    {
        var results = response.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .ToDictionary(r => r.CallId, r => r.Result);

        var calls = new List<object>();
        foreach (var call in response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>())
        {
            results.TryGetValue(call.CallId, out var result);
            calls.Add(new
            {
                tool = call.Name,
                arguments = call.Arguments,
                result,
            });
        }

        return calls;
    }
}

/// <summary>
/// The read-only tool surface the agent may call. Every method is HARD-SCOPED to the constructing <c>userId</c>
/// (<c>WHERE UserId == userId</c>) and opens a short-lived read context, so the agent can never reach another user's
/// data even if the model asks for it. Returned shapes are deliberately small/JSON-friendly for the model.
/// </summary>
internal sealed class VibeAgentTools(IReadDbContextFactory<MarketingDbContext> readDb, Guid userId)
{
    [Description("Lists the user's most recent marketing data pulls (GA4 / Search Console) with their status.")]
    public async Task<object> ListRecentPullsAsync(
        [Description("Maximum number of pulls to return (1-20).")] int limit = 10)
    {
        await using var db = readDb.Create();
        var capped = Math.Clamp(limit, 1, 20);
        var pulls = await db.DataPulls
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(capped)
            .Select(p => new
            {
                id = p.Id,
                source = p.Source.ToString(),
                status = p.Status.ToString(),
                completedAt = p.CompletedAt,
            })
            .ToListAsync();
        return pulls;
    }

    [Description("Lists the user's normalized metric snapshots, optionally filtered by source (ga4 | gsc).")]
    public async Task<object> ListSnapshotsAsync(
        [Description("Optional source filter: ga4 | gsc. Null = all sources.")] string? source = null,
        [Description("Maximum number of snapshots to return (1-50).")] int limit = 25)
    {
        await using var db = readDb.Create();
        var capped = Math.Clamp(limit, 1, 50);
        var query = db.MetricSnapshots.Where(s => s.UserId == userId);

        if (!string.IsNullOrWhiteSpace(source)
            && Enum.TryParse<Entities.PullSource>(source, ignoreCase: true, out var parsed))
        {
            query = query.Where(s => s.Source == parsed);
        }

        var rows = await query
            .OrderByDescending(s => s.RecordedAt)
            .Take(capped)
            .Select(s => new
            {
                source = s.Source.ToString(),
                metric = s.MetricName,
                dimension = s.Dimension,
                value = s.Value,
            })
            .ToListAsync();
        return rows;
    }

    [Description("Lists the user's saved AI analyses (headline summaries), optionally filtered by source.")]
    public async Task<object> ListAnalysesAsync(
        [Description("Optional source filter: ga4 | gsc. Null = all sources.")] string? source = null,
        [Description("Maximum number of analyses to return (1-20).")] int limit = 10)
    {
        await using var db = readDb.Create();
        var capped = Math.Clamp(limit, 1, 20);
        var query = db.MarketingAnalyses.Where(a => a.UserId == userId);

        if (!string.IsNullOrWhiteSpace(source)
            && Enum.TryParse<Entities.PullSource>(source, ignoreCase: true, out var parsed))
        {
            query = query.Where(a => a.Source == parsed);
        }

        var rows = await query
            .OrderByDescending(a => a.AnalyzedAt)
            .Take(capped)
            .Select(a => new
            {
                id = a.Id,
                source = a.Source.ToString(),
                summary = a.Summary,
                analyzedAt = a.AnalyzedAt,
            })
            .ToListAsync();
        return rows;
    }

    [Description("Gets one saved AI analysis in full (summary + structured insights JSON) by its id.")]
    public async Task<object?> GetAnalysisAsync(
        [Description("The analysis id (from list_analyses).")] Guid id)
    {
        await using var db = readDb.Create();
        var analysis = await db.MarketingAnalyses
            .Where(a => a.Id == id && a.UserId == userId)
            .Select(a => new
            {
                id = a.Id,
                source = a.Source.ToString(),
                summary = a.Summary,
                insights = a.InsightsJson,
                analyzedAt = a.AnalyzedAt,
            })
            .FirstOrDefaultAsync();
        return analysis;
    }
}
