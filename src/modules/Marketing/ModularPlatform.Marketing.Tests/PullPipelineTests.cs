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
    public async Task Unknown_source_is_rejected_by_validation()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"mkt-bad-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var start = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/marketing/pulls", token, new { source = "myspace" }));

        start.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
