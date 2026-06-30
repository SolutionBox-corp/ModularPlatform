using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Features.ReconcileStaleOperations;
using ModularPlatform.Operations.Features.Status;
using ModularPlatform.Operations.Messaging;
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
    public async Task Demo_operation_accept_is_idempotent_for_the_same_user_type_and_key()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"op-idem-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var key = $"demo-{Guid.CreateVersion7():N}";

        var firstRequest = fixture.Authed(HttpMethod.Post, "/v1/operations/demo", token);
        firstRequest.Headers.Add("Idempotency-Key", key);
        var first = await fixture.Client.SendAsync(firstRequest);

        var retryRequest = fixture.Authed(HttpMethod.Post, "/v1/operations/demo", token);
        retryRequest.Headers.Add("Idempotency-Key", key);
        var retry = await fixture.Client.SendAsync(retryRequest);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        retry.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var firstId = (await PlatformApiFactory.ReadData(first)).GetProperty("operationId").GetGuid();
        var retryId = (await PlatformApiFactory.ReadData(retry)).GetProperty("operationId").GetGuid();
        retryId.ShouldBe(firstId);

        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM operations WHERE \"IdempotencyKey\" = '{key}'")).ShouldBe(1);
    }

    [Fact]
    public async Task Demo_operation_rejects_an_oversized_idempotency_key_before_creating_an_operation()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"op-idem-long-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var key = new string('x', 257);

        var request = fixture.Authed(HttpMethod.Post, "/v1/operations/demo", token);
        request.Headers.Add("Idempotency-Key", key);
        var response = await fixture.Client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await fixture.ScalarAsync<long>(
            $"SELECT count(*)::bigint FROM operations WHERE \"IdempotencyKey\" = '{key}'")).ShouldBe(0);
    }

    [Fact]
    public async Task Demo_invoke_runs_short_worker_request_and_times_out_predictably()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"op-invoke-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var ok = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/operations/demo-invoke", token, new { input = 7 }));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(ok);
        data.GetProperty("score").GetInt32().ShouldBe(14);
        data.GetProperty("reason").GetString().ShouldBe("computed-in-worker");

        var timeout = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/operations/demo-invoke", token,
                new { input = 7, timeoutMs = 50, workDelayMs = 500 }));
        timeout.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        var problem = await timeout.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        problem.GetProperty("errorCode").GetString().ShouldBe("operations.invoke_timeout");
    }

    [Fact]
    public async Task Demo_invoke_validates_input_before_the_worker_message_is_invoked()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"op-invoke-invalid-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var response = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Post, "/v1/operations/demo-invoke", token, new { input = 99 }));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
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
    public async Task Operation_status_surfaces_failed_terminal_state_with_safe_error_details()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"opfailed-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();

        var operationId = await store.CreateAsync("generic-module-import", userId, CancellationToken.None);
        await store.MarkRunningAsync(operationId, CancellationToken.None);
        await store.FailAsync(operationId, "module.import_failed", "The import failed.", CancellationToken.None);

        var poll = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, $"/v1/operations/{operationId}", token));

        poll.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(poll);
        data.GetProperty("id").GetGuid().ShouldBe(operationId);
        data.GetProperty("type").GetString().ShouldBe("generic-module-import");
        data.GetProperty("status").GetString().ShouldBe("Failed");
        data.GetProperty("errorCode").GetString().ShouldBe("module.import_failed");
        data.GetProperty("errorDetail").GetString().ShouldBe("The import failed.");
        data.GetProperty("completedAt").ValueKind.ShouldNotBe(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task Operations_list_is_paged_owner_scoped_newest_first_and_has_empty_state()
    {
        var (_, token) = await fixture.RegisterAndLoginAsync($"op-list-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        var empty = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/operations", token));
        empty.StatusCode.ShouldBe(HttpStatusCode.OK);
        var emptyData = await PlatformApiFactory.ReadData(empty);
        emptyData.GetProperty("totalCount").GetInt64().ShouldBe(0);
        emptyData.GetProperty("items").GetArrayLength().ShouldBe(0);

        var first = await StartDemoAsync(token);
        await Task.Delay(20);
        var second = await StartDemoAsync(token);
        await Task.Delay(20);
        var third = await StartDemoAsync(token);

        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/operations?page=1&pageSize=2", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var data = await PlatformApiFactory.ReadData(list);
        data.GetProperty("page").GetInt32().ShouldBe(1);
        data.GetProperty("pageSize").GetInt32().ShouldBe(2);
        data.GetProperty("totalCount").GetInt64().ShouldBe(3);
        data.GetProperty("items").GetArrayLength().ShouldBe(2);
        data.GetProperty("items")[0].GetProperty("id").GetGuid().ShouldBe(third);
        data.GetProperty("items").EnumerateArray().Any(x => x.GetProperty("id").GetGuid() == first).ShouldBeFalse();

        var (_, otherToken) = await fixture.RegisterAndLoginAsync($"op-list-other-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");
        var otherList = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Get, "/v1/operations", otherToken));
        otherList.StatusCode.ShouldBe(HttpStatusCode.OK);
        var otherData = await PlatformApiFactory.ReadData(otherList);
        otherData.GetProperty("items").EnumerateArray()
            .Any(x => x.GetProperty("id").GetGuid() == second)
            .ShouldBeFalse();
    }

    [Fact]
    public async Task Operations_list_orders_created_at_ties_by_id_for_stable_paging()
    {
        var (userId, token) = await fixture.RegisterAndLoginAsync($"op-list-tie-{Guid.CreateVersion7():N}@x.com", "Sup3rSecret!");

        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        var first = await store.CreateAsync("generic-module-import", userId, CancellationToken.None);
        var second = await store.CreateAsync("generic-module-export", userId, CancellationToken.None);
        var third = await store.CreateAsync("generic-module-sync", userId, CancellationToken.None);

        var tiedCreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await fixture.ExecuteSqlAsync($"""
            UPDATE operations
            SET "CreatedAt" = '{tiedCreatedAt:O}', "UpdatedAt" = NULL
            WHERE "Id" IN ('{first}', '{second}', '{third}');
            """);

        var list = await fixture.Client.SendAsync(
            fixture.Authed(HttpMethod.Get, "/v1/operations?page=1&pageSize=3", token));

        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var items = (await PlatformApiFactory.ReadData(list))
            .GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetGuid())
            .ToArray();

        items.ShouldBe([third, second, first]);
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

    [Fact]
    public async Task Redelivered_worker_message_does_not_resurrect_a_failed_terminal_operation()
    {
        var userId = Guid.CreateVersion7();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();
        var handler = new RunDemoOperationHandler();

        var operationId = await store.CreateAsync("generic-module-export", userId, CancellationToken.None);
        await store.FailAsync(operationId, "module.export_failed", "The export failed.", CancellationToken.None);

        // This is what a redelivered durable work message does: it enters the worker from the top and tries
        // MarkRunning + Complete again. A terminal Failed operation must stay Failed with its original safe error.
        await handler.Handle(
            new RunDemoOperation(operationId),
            store,
            NullLogger<RunDemoOperationHandler>.Instance,
            CancellationToken.None);

        var status = await dispatcher.Query(new GetOperationStatusQuery(operationId, userId));
        status.Status.ShouldBe("Failed");
        status.ErrorCode.ShouldBe("module.export_failed");
        status.ErrorDetail.ShouldBe("The export failed.");
    }

    [Fact]
    public async Task Concurrent_worker_transitions_retry_xmin_conflicts_and_leave_one_terminal_state()
    {
        var userId = Guid.CreateVersion7();
        await using var setupScope = fixture.Services.CreateAsyncScope();
        var setupStore = setupScope.ServiceProvider.GetRequiredService<IOperationStore>();
        var dispatcher = setupScope.ServiceProvider.GetRequiredService<IDispatcher>();

        var operationId = await setupStore.CreateAsync("generic-module-race", userId, CancellationToken.None);
        await setupStore.MarkRunningAsync(operationId, CancellationToken.None);

        var suffix = Guid.CreateVersion7().ToString("N");
        var functionName = $"delay_operation_transition_{suffix}";
        var triggerName = $"delay_operation_transition_{suffix}";

        try
        {
            await fixture.ExecuteSqlAsync($"""
                CREATE OR REPLACE FUNCTION {functionName}()
                RETURNS trigger AS $$
                BEGIN
                    IF NEW."Id" = '{operationId}'::uuid THEN
                        PERFORM pg_sleep(0.2);
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER {triggerName}
                BEFORE UPDATE ON operations
                FOR EACH ROW
                EXECUTE FUNCTION {functionName}();
                """);

            async Task CompleteFromWorkerScopeAsync()
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
                await store.CompleteAsync(operationId, new { ok = true }, CancellationToken.None);
            }

            async Task FailFromWorkerScopeAsync()
            {
                await using var scope = fixture.Services.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
                await store.FailAsync(operationId, "module.concurrent_failure", "Concurrent failure.", CancellationToken.None);
            }

            await Task.WhenAll(CompleteFromWorkerScopeAsync(), FailFromWorkerScopeAsync());
        }
        finally
        {
            await fixture.ExecuteSqlAsync($"""
                DROP TRIGGER IF EXISTS {triggerName} ON operations;
                DROP FUNCTION IF EXISTS {functionName}();
                """);
        }

        var status = await dispatcher.Query(new GetOperationStatusQuery(operationId, userId));
        new[] { "Succeeded", "Failed" }.ShouldContain(status.Status);
    }

    [Fact]
    public async Task Worker_transition_on_missing_operation_surfaces_not_found()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();

        var ex = await Should.ThrowAsync<NotFoundException>(
            () => store.MarkRunningAsync(Guid.CreateVersion7(), CancellationToken.None));

        ex.ErrorCode.ShouldBe("operation.not_found");
    }

    [Fact]
    public async Task Reconcile_stale_operations_marks_only_old_non_terminal_operations_failed()
    {
        var userId = Guid.CreateVersion7();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var stalePending = await store.CreateAsync("generic-module-import", userId, CancellationToken.None);
        var staleRunning = await store.CreateAsync("generic-module-export", userId, CancellationToken.None);
        await store.MarkRunningAsync(staleRunning, CancellationToken.None);
        var recentPending = await store.CreateAsync("generic-module-recent", userId, CancellationToken.None);
        var terminalFailed = await store.CreateAsync("generic-module-terminal", userId, CancellationToken.None);
        await store.FailAsync(terminalFailed, "module.original_failure", "Original failure.", CancellationToken.None);

        await fixture.ExecuteSqlAsync($"""
            UPDATE operations
            SET "CreatedAt" = now() - interval '3 hours', "UpdatedAt" = NULL
            WHERE "Id" = '{stalePending}';

            UPDATE operations
            SET "UpdatedAt" = now() - interval '3 hours'
            WHERE "Id" = '{staleRunning}';

            UPDATE operations
            SET "CreatedAt" = now() - interval '3 hours', "UpdatedAt" = now() - interval '3 hours'
            WHERE "Id" = '{terminalFailed}';
            """);

        var result = await dispatcher.Send(new ReconcileStaleOperationsCommand(StaleAfterMinutes: 60));

        result.FailedCount.ShouldBe(2);
        result.CapReached.ShouldBeFalse();

        var pendingStatus = await dispatcher.Query(new GetOperationStatusQuery(stalePending, userId));
        pendingStatus.Status.ShouldBe("Failed");
        pendingStatus.ErrorCode.ShouldBe("operation.stale_reconciled");

        var runningStatus = await dispatcher.Query(new GetOperationStatusQuery(staleRunning, userId));
        runningStatus.Status.ShouldBe("Failed");
        runningStatus.ErrorCode.ShouldBe("operation.stale_reconciled");

        var recentStatus = await dispatcher.Query(new GetOperationStatusQuery(recentPending, userId));
        recentStatus.Status.ShouldBe("Pending");

        var terminalStatus = await dispatcher.Query(new GetOperationStatusQuery(terminalFailed, userId));
        terminalStatus.Status.ShouldBe("Failed");
        terminalStatus.ErrorCode.ShouldBe("module.original_failure");
        terminalStatus.ErrorDetail.ShouldBe("Original failure.");
    }

    [Fact]
    public async Task Reconcile_stale_operations_uses_id_tie_breaker_when_cap_cuts_same_timestamp_rows()
    {
        var userId = Guid.CreateVersion7();
        await using var scope = fixture.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IOperationStore>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        var first = await store.CreateAsync("generic-module-first", userId, CancellationToken.None);
        var second = await store.CreateAsync("generic-module-second", userId, CancellationToken.None);
        var third = await store.CreateAsync("generic-module-third", userId, CancellationToken.None);
        var orderedIds = new[] { first, second, third }.OrderBy(id => id).ToArray();

        await fixture.ExecuteSqlAsync($"""
            UPDATE operations
            SET "CreatedAt" = now() - interval '3 hours', "UpdatedAt" = NULL
            WHERE "Id" IN ('{first}', '{second}', '{third}');
            """);

        var result = await dispatcher.Send(new ReconcileStaleOperationsCommand(StaleAfterMinutes: 60, Cap: 2));

        result.FailedCount.ShouldBe(2);
        result.CapReached.ShouldBeTrue();

        foreach (var id in orderedIds.Take(2))
        {
            var status = await dispatcher.Query(new GetOperationStatusQuery(id, userId));
            status.Status.ShouldBe("Failed");
            status.ErrorCode.ShouldBe("operation.stale_reconciled");
        }

        var skipped = await dispatcher.Query(new GetOperationStatusQuery(orderedIds[2], userId));
        skipped.Status.ShouldBe("Pending");
    }

    private async Task<Guid> StartDemoAsync(string token)
    {
        var start = await fixture.Client.SendAsync(fixture.Authed(HttpMethod.Post, "/v1/operations/demo", token));
        start.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        return (await PlatformApiFactory.ReadData(start)).GetProperty("operationId").GetGuid();
    }
}
