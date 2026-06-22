namespace ModularPlatform.Marketing.Integrations;

/// <summary>An AI analysis of a set of metrics: a short headline plus structured insights/recommendations as JSON.</summary>
internal sealed record AnalysisResult(string Summary, string InsightsJson);

/// <summary>
/// Anti-corruption port to the analysis LLM (Claude). The real implementation wraps <c>IChatClient</c>; the
/// deterministic <c>FakeMarketingAiGateway</c> replaces it under <c>Marketing:UseFakeGateways</c> so the analysis
/// pipeline is testable without an API key.
/// </summary>
internal interface IMarketingAiGateway
{
    Task<AnalysisResult> AnalyzeAsync(string source, string metricsJson, CancellationToken ct);
}
