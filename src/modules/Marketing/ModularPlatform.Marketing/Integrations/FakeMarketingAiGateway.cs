using System.Text.Json;

namespace ModularPlatform.Marketing.Integrations;

/// <summary>
/// Deterministic in-memory analysis gateway for dev/tests (registered under <c>Marketing:UseFakeGateways=true</c>).
/// Produces a fixed-shape summary + insights JSON derived from the source so the pull → analysis pipeline is
/// exercisable without a Claude API key.
/// </summary>
internal sealed class FakeMarketingAiGateway : IMarketingAiGateway
{
    public Task<AnalysisResult> AnalyzeAsync(string source, string metricsJson, CancellationToken ct)
    {
        var summary = $"Automated {source.ToUpperInvariant()} analysis: traffic and conversions are within the expected range.";
        var insights = JsonSerializer.Serialize(new
        {
            source,
            highlights = new[] { "Organic search is the top channel.", "Lead conversion rate is steady." },
            recommendations = new[] { "Double down on top-performing keywords.", "Test a stronger CTA on the landing page." },
        });
        return Task.FromResult(new AnalysisResult(summary, insights));
    }
}
