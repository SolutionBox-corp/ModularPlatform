using ModularPlatform.Cqrs;

namespace ModularPlatform.Operations.Features.Demo;

public sealed record StartDemoOperationCommand(Guid UserId) : ICommand<StartDemoOperationResponse>;

public sealed record StartDemoOperationResponse(Guid OperationId);
