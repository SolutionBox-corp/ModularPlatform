using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Marketing.Tests;

/// <summary>
/// The marketing pull pipeline end-to-end (canonical 202 + status pattern): triggering a GA4 pull returns 202 with a
/// Location to the status endpoint; the durable worker calls the (fake) gateway, persists normalized snapshots, and
/// drives the pull to Completed; the owner polls the status and lists the snapshots; another user cannot see the pull
/// (RLS-isolated → 404).
/// </summary>
[Collection("Integration")]
public sealed class PullPipelineTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Ga4_pull_is_accepted_runs_on_the_worker_persists_snapshots_and_is_owner_scoped()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-pull-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        // Accept: 202 + Location header to the status endpoint + the pull id in the body.
        var start = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/pulls", token, new { source = "ga4" }));
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var dataPullId = (await PlatformApiFactory.ReadData(start)).GetProperty("dataPullId").GetGuid();
        start.Headers.Location!.ToString().ShouldContain(dataPullId.ToString());

        // Poll the status endpoint until the durable worker drives the pull to a terminal state.
        var status = "Pending";
        for (var attempt = 0; attempt < 60 && status is "Pending" or "Running"; attempt++)
        {
            var poll = await fixture.Client.SendAsync(
                fixture.Authed(HttpMethod.Get, $"/v1/marketing/pulls/{dataPullId}", token));
            poll.StatusCode.ShouldBe(HttpStatusCode.OK);
            status = (await PlatformApiFactory.ReadData(poll)).GetProperty("status").GetString()!;
            if (status is "Pending" or "Running")
            {
                await Task.Delay(500);
            }
        }

        status.ShouldBe("Completed");

        // The fake GA4 gateway produced normalized snapshots — they are listed for the owner.
        var snapshots = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/snapshots?source=ga4", token));
        snapshots.StatusCode.ShouldBe(HttpStatusCode.OK);
        var totalCount = (await PlatformApiFactory.ReadData(snapshots)).GetProperty("totalCount").GetInt64();
        totalCount.ShouldBeGreaterThan(0);

        // The completed pull triggers the analysis worker (fake AI), which persists a MarketingAnalysis — poll for it.
        long analysisCount = 0;
        for (var attempt = 0; attempt < 60 && analysisCount == 0; attempt++)
        {
            var analyses = await fixture.Client.SendAsync(
                fixture.Authed(HttpMethod.Get, "/v1/marketing/analyses", token));
            analyses.StatusCode.ShouldBe(HttpStatusCode.OK);
            analysisCount = (await PlatformApiFactory.ReadData(analyses)).GetProperty("totalCount").GetInt64();
            if (analysisCount == 0)
            {
                await Task.Delay(500);
            }
        }

        analysisCount.ShouldBeGreaterThan(0);

        // RLS owner-scoping: a DIFFERENT user cannot see the pull — it is simply not found.
        var (_, otherToken) = await fixture.RegisterAndLoginAsync($"mkt-other-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var foreign = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, $"/v1/marketing/pulls/{dataPullId}", otherToken));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trigger_pull_persists_the_requested_date_window()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-window-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var start = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/pulls", token, new
            {
                source = "ga4",
                startDate = "2026-06-01",
                endDate = "2026-06-07",
            }));

        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var dataPullId = (await PlatformApiFactory.ReadData(start)).GetProperty("dataPullId").GetGuid();

        var paramsJson = await fixture.ScalarAsync<string>(
            $"""SELECT "ParamsJson"::text FROM data_pulls WHERE "Id" = '{dataPullId}'""");

        paramsJson.ShouldContain("\"Start\": \"2026-06-01\"");
        paramsJson.ShouldContain("\"End\": \"2026-06-07\"");
    }

    [Fact]
    public async Task Marketing_read_lists_order_same_timestamp_rows_by_id_for_stable_paging()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync(
            $"mkt-stable-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var pullIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var snapshotIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        var analysisIds = new[] { Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7() };
        const string sameInstant = "2030-01-01 00:00:00+00";

        foreach (var id in pullIds)
        {
            await fixture.ExecuteSqlAsync(
                "INSERT INTO data_pulls (\"Id\", \"UserId\", \"Source\", \"Status\", \"CreatedAt\") " +
                $"VALUES ('{id}', '{userId}', 'Ga4', 'Completed', timestamp with time zone '{sameInstant}')");
        }

        foreach (var id in snapshotIds)
        {
            await fixture.ExecuteSqlAsync(
                "INSERT INTO metric_snapshots (\"Id\", \"UserId\", \"DataPullId\", \"Source\", \"MetricName\", \"Value\", \"RecordedAt\") " +
                $"VALUES ('{id}', '{userId}', '{pullIds[0]}', 'Ga4', 'ga4:sessions', 42, timestamp with time zone '{sameInstant}')");
        }

        foreach (var id in analysisIds)
        {
            await fixture.ExecuteSqlAsync(
                "INSERT INTO marketing_analyses (\"Id\", \"UserId\", \"Source\", \"Summary\", \"AnalyzedAt\", \"CreatedAt\") " +
                $"VALUES ('{id}', '{userId}', 'Ga4', 'Stable analysis', timestamp with time zone '{sameInstant}', timestamp with time zone '{sameInstant}')");
        }

        var pulls = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/pulls?page=1&pageSize=3", token));
        pulls.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(pulls)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe(pullIds.OrderByDescending(id => id).ToArray());

        var snapshots = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/snapshots?source=ga4&page=1&pageSize=3", token));
        snapshots.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(snapshots)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe(snapshotIds.OrderByDescending(id => id).ToArray());

        var analyses = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/marketing/analyses?page=1&pageSize=3", token));
        analyses.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await PlatformApiFactory.ReadData(analyses)).GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ShouldBe(analysisIds.OrderByDescending(id => id).ToArray());
    }

    [Fact]
    public async Task Unknown_source_is_rejected_by_validation()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-bad-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var start = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/pulls", token, new { source = "myspace" }));

        start.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await start.Content.ReadAsStringAsync()).ShouldContain("marketing.source_unknown");
    }

    [Theory]
    [InlineData("posthog")]
    [InlineData("reddit")]
    [InlineData("trends")]
    public async Task Known_but_unwired_sources_are_rejected_before_creating_a_pull(string source)
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"mkt-unwired-{source}-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var start = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/pulls", token, new { source }));

        start.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await start.Content.ReadAsStringAsync()).ShouldContain("marketing.source_not_supported");
        (await fixture.ScalarAsync<long>($"SELECT count(*)::bigint FROM data_pulls WHERE \"UserId\" = '{userId}'"))
            .ShouldBe(0);
    }
}
