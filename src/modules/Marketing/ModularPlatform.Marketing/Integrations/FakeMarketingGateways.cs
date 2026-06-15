using System.Text.Json;

namespace ModularPlatform.Marketing.Integrations;

/// <summary>
/// Deterministic in-memory GA4 gateway for dev/tests (registered under <c>Marketing:UseFakeGateways=true</c>). Returns
/// a small fixed set of channel metrics so the whole pull → snapshot → analysis pipeline is exercisable without
/// Google credentials. Values are seeded from the date window so a wider range yields proportionally larger numbers.
/// </summary>
internal sealed class FakeGa4Gateway : IGa4Gateway
{
    public Task<PullResult> RunReportAsync(Guid userId, Ga4PullParams parameters, CancellationToken ct)
    {
        var days = Math.Max(1, parameters.EndDate.DayNumber - parameters.StartDate.DayNumber + 1);
        var rows = new List<MetricRow>
        {
            new("ga4:sessions", "Organic Search", 120d * days, """{"users":90,"channel":"Organic Search"}"""),
            new("ga4:sessions", "Direct", 60d * days, """{"users":48,"channel":"Direct"}"""),
            new("ga4:lead_submitted", null, 3d * days, """{"conversionRate":0.012}"""),
        };
        var raw = JsonSerializer.Serialize(new { source = "ga4", parameters.StartDate, parameters.EndDate, rows });
        return Task.FromResult(new PullResult(raw, rows));
    }
}

/// <summary>Deterministic in-memory Search Console gateway for dev/tests. Returns a few top queries by clicks.</summary>
internal sealed class FakeGscGateway : IGscGateway
{
    public Task<PullResult> SearchAnalyticsAsync(Guid userId, GscPullParams parameters, CancellationToken ct)
    {
        var days = Math.Max(1, parameters.EndDate.DayNumber - parameters.StartDate.DayNumber + 1);
        var rows = new List<MetricRow>
        {
            new("gsc:clicks", "ksef faktury", 15d * days, """{"impressions":420,"ctr":0.036,"position":4.2}"""),
            new("gsc:clicks", "ai automatizace", 9d * days, """{"impressions":310,"ctr":0.029,"position":6.1}"""),
        };
        var raw = JsonSerializer.Serialize(new { source = "gsc", parameters.StartDate, parameters.EndDate, rows });
        return Task.FromResult(new PullResult(raw, rows));
    }
}
