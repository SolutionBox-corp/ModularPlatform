using System.Net;
using Microsoft.Extensions.DependencyInjection;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Features.Status;
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

    [Fact]
    public async Task Operation_status_is_owner_scoped_at_the_app_layer_even_when_rls_is_bypassed()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"opappf-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var start = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/operations/demo", token));
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var operationId = (await PlatformApiFactory.ReadData(start)).GetProperty("operationId").GetGuid();

        var intruderId = Guid.CreateVersion7();

        // In-process dispatch under the SYSTEM context (RLS bypassed, exactly as the Worker runs) with a foreign
        // user id: only the app-level owner filter can stop the leak — proves safety even when a deployment runs
        // with Persistence:Rls:Enabled=false.
        await using var scope = fixture.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await Should.ThrowAsync<NotFoundException>(
            () => dispatcher.Query(new GetOperationStatusQuery(operationId, intruderId)));
    }

    [Fact]
    public async Task A_terminal_operation_is_not_resurrected_by_a_duplicate_worker_transition()
    {
        var userId = Guid.CreateVersion7();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var operationId = await store.CreateAsync("demo", userId, CancellationToken.None);
        await store.CompleteAsync(operationId, new { ok = true }, CancellationToken.None);

        // A redelivered/duplicate worker message would call MarkRunning again — a terminal operation must stay
        // terminal (Wolverine inbox dedups most duplicates, but a transition store must be its own backstop).
        await store.MarkRunningAsync(operationId, CancellationToken.None);

        var status = await dispatcher.Query(new GetOperationStatusQuery(operationId, userId));
        status.Status.ShouldBe("Succeeded");
    }
}
