using System.Net;
using ModularPlatform.IntegrationTesting;
using Shouldly;

namespace ModularPlatform.Operations.Tests;

/// <summary>
/// The long-running 202 + status-polling pattern end-to-end: starting the demo operation returns 202 Accepted
/// with a Location pointing at the status endpoint; the durable worker drives it to Succeeded; the owner polls
/// the status; and another user cannot see the operation (RLS-isolated → 404).
/// </summary>
[Collection("Integration")]
public sealed class OperationsTests(PlatformApiFactory fixture)
{
    [Fact]
    public async Task Demo_operation_is_accepted_runs_on_the_worker_and_is_owner_scoped()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"op-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        // Accept: 202 + a Location header to the status endpoint + the operation id in the body.
        var start = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/operations/demo", token));
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var operationId = (await PlatformApiFactory.ReadData(start)).GetProperty("operationId").GetGuid();
        start.Headers.Location!.ToString().ShouldContain(operationId.ToString());

        // Poll the status endpoint until the durable worker drives the operation to a terminal state.
        string status = "Pending";
        for (var attempt = 0; attempt < 60 && status is "Pending" or "Running"; attempt++)
        {
            var poll = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/operations/{operationId}", token));
            poll.StatusCode.ShouldBe(HttpStatusCode.OK);
            status = (await PlatformApiFactory.ReadData(poll)).GetProperty("status").GetString()!;
            if (status is "Pending" or "Running")
            {
                await Task.Delay(500);
            }
        }

        status.ShouldBe("Succeeded");

        // RLS owner-scoping: a DIFFERENT user cannot see the operation — it is simply not found.
        var (_, otherToken) = await fixture.RegisterAndLoginAsync($"other-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var foreign = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/operations/{operationId}", otherToken));
        foreign.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
