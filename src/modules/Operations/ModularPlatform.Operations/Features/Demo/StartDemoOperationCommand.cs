using ModularPlatform.Cqrs;

namespace ModularPlatform.Operations.Features.Demo;

public sealed record StartDemoOperationCommand(Guid UserId, string? IdempotencyKey = null)
    : ICommand<StartDemoOperationResponse>;

public sealed record StartDemoOperationResponse(Guid OperationId);
