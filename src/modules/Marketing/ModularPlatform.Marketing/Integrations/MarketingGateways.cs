using ModularPlatform.Marketing.Entities;

namespace ModularPlatform.Marketing.Integrations;

/// <summary>
/// One normalized metric data point, the common shape every marketing source maps onto before it is persisted as a
/// <see cref="MetricSnapshot"/>. Keeps provider-specific quirks inside the gateway (anti-corruption layer).
/// </summary>
internal sealed record MetricRow(string MetricName, string? Dimension, double Value, string? DetailJson);

/// <summary>The outcome of a pull: the verbatim provider payload + the normalized rows projected from it.</summary>
internal sealed record PullResult(string RawJson, IReadOnlyList<MetricRow> Metrics);

/// <summary>Parameters for a GA4 report pull (a date window over the user's connected property).</summary>
internal sealed record Ga4PullParams(DateOnly StartDate, DateOnly EndDate);

/// <summary>Parameters for a Search Console pull (a date window over the user's connected site).</summary>
internal sealed record GscPullParams(DateOnly StartDate, DateOnly EndDate);

/// <summary>
/// Anti-corruption port to the GA4 Data API. The real implementation wraps the Google SDK; the in-memory
/// <c>FakeGa4Gateway</c> replaces it under <c>Marketing:UseFakeGateways</c> so the pull pipeline is testable
/// without live credentials.
/// </summary>
internal interface IGa4Gateway
{
    Task<PullResult> RunReportAsync(Guid userId, Ga4PullParams parameters, CancellationToken ct);
}

/// <summary>Anti-corruption port to the Google Search Console API. Real impl wraps the SDK; fake replaces it for tests.</summary>
internal interface IGscGateway
{
    Task<PullResult> SearchAnalyticsAsync(Guid userId, GscPullParams parameters, CancellationToken ct);
}
