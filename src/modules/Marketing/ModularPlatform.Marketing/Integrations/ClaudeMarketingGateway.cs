using System.Text.Json;
using Anthropic.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ModularPlatform.Marketing.Integrations;

/// <summary>Configuration for the Claude-backed analysis gateway, bound from <c>Marketing:Claude</c>.</summary>
internal sealed class MarketingClaudeOptions
{
    public const string SectionName = "Marketing:Claude";

    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-opus-4-8";
}

/// <summary>
/// Real analysis gateway: asks Claude (via <c>IChatClient</c>) for a short summary + structured insights JSON over a
/// set of metrics. The single LLM call is wrapped here (anti-corruption); the caller persists the result. Wired only
/// when <c>Marketing:UseFakeGateways=false</c>, so a missing key surfaces at call time rather than at boot in tests.
/// </summary>
internal sealed class ClaudeMarketingGateway(IOptions<MarketingClaudeOptions> options) : IMarketingAiGateway
{
    private readonly MarketingClaudeOptions _options = options.Value;

    public async Task<AnalysisResult> AnalyzeAsync(string source, string metricsJson, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new InvalidOperationException("Marketing:Claude:ApiKey is not configured.");
        }

        using IChatClient client = new ChatClientBuilder(new AnthropicClient(_options.ApiKey).Messages)
            .ConfigureOptions(o => o.ModelId ??= _options.Model)
            .Build();

        const string shape =
            "{\"summary\": \"<one-sentence headline>\", " +
            "\"insights\": {\"highlights\": [\"...\"], \"recommendations\": [\"...\"]}}";
        var prompt =
            $"You are a marketing analyst. Analyze the following {source} metrics and respond with STRICT JSON only, " +
            "no prose, in this exact shape:\n" + shape + "\n\nMetrics:\n" + metricsJson;

        var response = await client.GetResponseAsync(prompt, cancellationToken: ct);
        var text = (response.Text ?? string.Empty).Trim();

        return Parse(text);
    }

    private static AnalysisResult Parse(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var summary = root.TryGetProperty("summary", out var s) ? s.GetString() ?? text : text;
            var insights = root.TryGetProperty("insights", out var i) ? i.GetRawText() : "{}";
            return new AnalysisResult(summary, insights);
        }
        catch (JsonException)
        {
            // The model didn't return clean JSON — keep its text as the summary so the analysis is still usable.
            return new AnalysisResult(text, "{}");
        }
    }
}
