using ModularPlatform.Cqrs;

namespace ModularPlatform.Operations.Features.DemoInvoke;

public sealed record InvokeDemoCheckCommand(Guid UserId, int Input, int TimeoutMs, int WorkDelayMs)
    : ICommand<InvokeDemoCheckResponse>;

public sealed record InvokeDemoCheckRequest(int Input, int? TimeoutMs = null, int? WorkDelayMs = null);

public sealed record InvokeDemoCheckResponse(int Score, string Reason);
