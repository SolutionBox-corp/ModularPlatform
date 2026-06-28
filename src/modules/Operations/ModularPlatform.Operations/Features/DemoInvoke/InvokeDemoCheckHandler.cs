using ModularPlatform.Cqrs;
using ModularPlatform.Operations.Messaging;
using Wolverine;

namespace ModularPlatform.Operations.Features.DemoInvoke;

/// <summary>
/// Canonical UC97 request-response over Wolverine. The handler sets a short timeout and invokes a pure worker
/// message. If the work can take longer or has side effects, use the Operations 202/status pattern instead.
/// </summary>
internal sealed class InvokeDemoCheckHandler(IMessageBus bus)
    : ICommandHandler<InvokeDemoCheckCommand, InvokeDemoCheckResponse>
{
    public async Task<InvokeDemoCheckResponse> Handle(InvokeDemoCheckCommand command, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(command.TimeoutMs));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        try
        {
            var result = await bus.InvokeAsync<DemoQuickCheckResult>(
                new DemoQuickCheck(command.UserId, command.Input, command.WorkDelayMs),
                linked.Token);

            return new InvokeDemoCheckResponse(result.Score, result.Reason);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new BusinessRuleException("operations.invoke_timeout", "The quick worker request timed out.");
        }
    }
}
