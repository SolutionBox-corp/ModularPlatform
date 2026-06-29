using Microsoft.Extensions.Logging.Abstractions;
using ModularPlatform.Abstractions;
using ModularPlatform.Operations.Messaging;
using Shouldly;

namespace ModularPlatform.Operations.Tests;

/// <summary>
/// A long-running worker must drive its operation to a TERMINAL state even when the work throws — otherwise the
/// operation is stuck <c>Running</c> forever and the caller polls indefinitely. Pure unit test over the canonical
/// worker via a hand-written <see cref="IOperationStore"/> fake (the port has a tiny, stable surface).
/// </summary>
public sealed class OperationWorkerFailureTests
{
    [Fact]
    public async Task Worker_marks_the_operation_failed_when_the_work_throws()
    {
        var operationId = Guid.CreateVersion7();
        var store = new RecordingOperationStore(failOnComplete: true);
        var handler = new RunDemoOperationHandler();

        // Must NOT propagate the work exception as a forever-Running operation: the failure is recorded terminally.
        await handler.Handle(new RunDemoOperation(operationId), store, NullLogger<RunDemoOperationHandler>.Instance, CancellationToken.None);

        store.FailedOperationId.ShouldBe(operationId);
        store.FailedErrorCode.ShouldBe("operation.failed");
    }

    [Fact]
    public async Task Worker_propagates_when_failure_state_cannot_be_persisted_so_wolverine_can_retry()
    {
        var operationId = Guid.CreateVersion7();
        var store = new RecordingOperationStore(failOnComplete: true, failOnFail: true);
        var handler = new RunDemoOperationHandler();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => handler.Handle(new RunDemoOperation(operationId), store, NullLogger<RunDemoOperationHandler>.Instance, CancellationToken.None));

        ex.Message.ShouldBe("fail write blew up");
        store.FailedOperationId.ShouldBe(operationId);
    }

    private sealed class RecordingOperationStore(bool failOnComplete, bool failOnFail = false) : IOperationStore
    {
        public Guid? FailedOperationId { get; private set; }
        public string? FailedErrorCode { get; private set; }

        public Task<Guid> CreateAsync(string type, Guid userId, CancellationToken ct, string? idempotencyKey = null) =>
            Task.FromResult(Guid.CreateVersion7());

        public Task MarkRunningAsync(Guid operationId, CancellationToken ct) => Task.CompletedTask;

        public Task CompleteAsync(Guid operationId, object? result, CancellationToken ct) =>
            failOnComplete ? throw new InvalidOperationException("work blew up") : Task.CompletedTask;

        public Task FailAsync(Guid operationId, string errorCode, string? detail, CancellationToken ct)
        {
            FailedOperationId = operationId;
            FailedErrorCode = errorCode;
            if (failOnFail)
            {
                throw new InvalidOperationException("fail write blew up");
            }

            return Task.CompletedTask;
        }
    }
}
