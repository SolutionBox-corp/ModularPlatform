using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Entities;
using ModularPlatform.Operations.Messaging;
using ModularPlatform.Operations.Persistence;
using Wolverine.EntityFrameworkCore;

namespace ModularPlatform.Operations.Features.Demo;

/// <summary>
/// CANONICAL long-running accept: creates a Pending operation owned by the caller AND publishes the durable work
/// message in ONE transaction (outbox), then returns its id. The endpoint replies 202; the worker does the real
/// work and transitions the operation; the caller polls the status endpoint. Never do the slow work here.
/// </summary>
internal sealed class StartDemoOperationHandler(IDbContextOutbox<OperationsDbContext> outbox)
    : ICommandHandler<StartDemoOperationCommand, StartDemoOperationResponse>
{
    public async Task<StartDemoOperationResponse> Handle(StartDemoOperationCommand command, CancellationToken ct)
    {
        var operation = new Operation
        {
            UserId = command.UserId,
            Type = "demo",
            Status = OperationStatus.Pending,
        };
        outbox.DbContext.Operations.Add(operation);

        await outbox.PublishAsync(new RunDemoOperation(operation.Id));
        await outbox.SaveChangesAndFlushMessagesAsync();

        return new StartDemoOperationResponse(operation.Id);
    }
}
