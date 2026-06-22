using System.Globalization;
using System.Text.Json;
using Google.Apis.AnalyticsData.v1beta;
using Google.Apis.AnalyticsData.v1beta.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.SearchConsole.v1;
using Google.Apis.SearchConsole.v1.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Options;

namespace ModularPlatform.Marketing.Integrations;

/// <summary>
/// Config-driven target + credentials for the real Google marketing gateways, bound from <c>Marketing:Google</c>.
/// </summary>
/// <remarks>
/// Auth is a single shared SERVICE ACCOUNT reading ONE configured GA4 property + ONE configured Search Console site.
/// A per-user OAuth connection model (each tenant connecting their own GA4 property / GSC site) is the
/// productization follow-up and is deliberately OUT OF SCOPE here — the gateways already receive <c>userId</c> so
/// that connection can be threaded in later without changing the port. Mirror <c>MarketingClaudeOptions</c>.
/// </remarks>
internal sealed class MarketingGoogleOptions
{
    public const string SectionName = "Marketing:Google";

    /// <summary>Path to a service-account JSON key file. Takes precedence over <see cref="ServiceAccountJson"/>.</summary>
    public string ServiceAccountJsonPath { get; set; } = string.Empty;

    /// <summary>Inline service-account JSON (e.g. fed from a secret store). Used when the path is empty.</summary>
    public string ServiceAccountJson { get; set; } = string.Empty;

    /// <summary>The numeric GA4 property id (the gateway prepends <c>properties/</c>). e.g. <c>123456789</c>.</summary>
    public string Ga4PropertyId { get; set; } = string.Empty;

    /// <summary>
    /// The Search Console site URL exactly as verified in GSC — a URL-prefix property (<c>https://example.com/</c>)
    /// or a Domain property (<c>sc-domain:example.com</c>).
    /// </summary>
    public string GscSiteUrl { get; set; } = string.Empty;
}

/// <summary>
/// Shared service-account credential builder for the Google gateways. Keeps the Google SDK auth quirks inside the
/// anti-corruption layer. Fails fast (mirrors <c>ClaudeMarketingGateway</c>'s missing-key guard) when no credential
/// source is configured.
/// </summary>
internal static class GoogleCredentialFactory
{
    public static GoogleCredential Create(MarketingGoogleOptions options, IEnumerable<string> scopes)
    {
        GoogleCredential credential;
        if (!string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath))
        {
            credential = CredentialFactory.FromFile<GoogleCredential>(options.ServiceAccountJsonPath);
        }
        else if (!string.IsNullOrWhiteSpace(options.ServiceAccountJson))
        {
            credential = CredentialFactory.FromJson<GoogleCredential>(options.ServiceAccountJson);
        }
        else
        {
            throw new InvalidOperationException(
                "Marketing:Google service-account credentials are not configured " +
                "(set Marketing:Google:ServiceAccountJsonPath or Marketing:Google:ServiceAccountJson).");
        }

        return credential.CreateScoped(scopes);
    }
}

/// <summary>
/// Real GA4 Data API gateway over the official <c>Google.Apis.AnalyticsData.v1beta</c> SDK
/// (<see cref="AnalyticsDataService"/> → <c>properties.runReport</c>). All SDK types stay inside this gateway
/// (anti-corruption). Wired only when <c>Marketing:UseFakeGateways=false</c>; a missing key/target surfaces at call
/// time, not at boot, so the test harness (fakes) never needs Google credentials.
/// </summary>
/// <remarks>
/// <paramref name="userId"/> is accepted for a FUTURE per-user OAuth connection model; today the gateway reports on
/// the single <c>Marketing:Google:Ga4PropertyId</c> configured property regardless of user. See
/// <see cref="MarketingGoogleOptions"/>.
/// </remarks>
internal sealed class RealGa4Gateway(IOptions<MarketingGoogleOptions> options) : IGa4Gateway
{
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/analytics.readonly"];
    private readonly MarketingGoogleOptions _options = options.Value;

    public async Task<PullResult> RunReportAsync(Guid userId, Ga4PullParams parameters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.Ga4PropertyId))
        {
            throw new InvalidOperationException("Marketing:Google:Ga4PropertyId is not configured.");
        }

        var credential = GoogleCredentialFactory.Create(_options, Scopes);
        using var service = new AnalyticsDataService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ModularPlatform.Marketing",
        });

        var request = new RunReportRequest
        {
            DateRanges =
            [
                new DateRange
                {
                    StartDate = parameters.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EndDate = parameters.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                },
            ],
            Dimensions = [new Dimension { Name = "sessionDefaultChannelGroup" }],
            Metrics =
            [
                new Metric { Name = "sessions" },
                new Metric { Name = "totalUsers" },
                new Metric { Name = "conversions" },
            ],
        };

        var property = "properties/" + _options.Ga4PropertyId;
        var response = await service.Properties.RunReport(request, property).ExecuteAsync(ct);

        var metrics = MapGa4(response);
        var raw = response.ToString() ?? "{}";
        return new PullResult(raw, metrics);
    }

    private static List<MetricRow> MapGa4(RunReportResponse response)
    {
        var rows = new List<MetricRow>();
        if (response.Rows is null)
        {
            return rows;
        }

        // Column order mirrors the request's Metrics list; index by position into MetricHeaders.
        var metricNames = response.MetricHeaders?.Select(h => h.Name).ToArray() ?? [];
        foreach (var row in response.Rows)
        {
            var dimension = row.DimensionValues is { Count: > 0 } ? row.DimensionValues[0].Value : null;
            var values = row.MetricValues ?? [];
            for (var i = 0; i < values.Count && i < metricNames.Length; i++)
            {
                var value = double.TryParse(
                    values[i].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0d;
                rows.Add(new MetricRow($"ga4:{metricNames[i]}", dimension, value, null));
            }
        }

        return rows;
    }
}

/// <summary>
/// Real Search Console gateway over the official <c>Google.Apis.SearchConsole.v1</c> SDK
/// (<see cref="SearchConsoleService"/> → <c>searchanalytics.query</c>). All SDK types stay inside this gateway
/// (anti-corruption). Wired only when <c>Marketing:UseFakeGateways=false</c>.
/// </summary>
/// <remarks>
/// <paramref name="userId"/> is accepted for a FUTURE per-user OAuth connection model; today the gateway queries the
/// single <c>Marketing:Google:GscSiteUrl</c> configured site regardless of user. See <see cref="MarketingGoogleOptions"/>.
/// </remarks>
internal sealed class RealGscGateway(IOptions<MarketingGoogleOptions> options) : IGscGateway
{
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/webmasters.readonly"];
    private readonly MarketingGoogleOptions _options = options.Value;

    public async Task<PullResult> SearchAnalyticsAsync(Guid userId, GscPullParams parameters, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.GscSiteUrl))
        {
            throw new InvalidOperationException("Marketing:Google:GscSiteUrl is not configured.");
        }

        var credential = GoogleCredentialFactory.Create(_options, Scopes);
        using var service = new SearchConsoleService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "ModularPlatform.Marketing",
        });

        var request = new SearchAnalyticsQueryRequest
        {
            StartDate = parameters.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            EndDate = parameters.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Dimensions = ["query"],
            RowLimit = 25,
        };

        var response = await service.Searchanalytics.Query(request, _options.GscSiteUrl).ExecuteAsync(ct);

        var metrics = MapGsc(response);
        var raw = response.ToString() ?? "{}";
        return new PullResult(raw, metrics);
    }

    private static List<MetricRow> MapGsc(SearchAnalyticsQueryResponse response)
    {
        var rows = new List<MetricRow>();
        if (response.Rows is null)
        {
            return rows;
        }

        foreach (var row in response.Rows)
        {
            var query = row.Keys is { Count: > 0 } ? row.Keys[0] : null;
            var detail = JsonSerializer.Serialize(new
            {
                impressions = row.Impressions,
                ctr = row.Ctr,
                position = row.Position,
            });
            rows.Add(new MetricRow("gsc:clicks", query, row.Clicks ?? 0d, detail));
        }

        return rows;
    }
}
