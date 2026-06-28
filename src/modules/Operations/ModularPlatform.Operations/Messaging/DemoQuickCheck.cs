namespace ModularPlatform.Operations.Messaging;

/// <summary>
/// Pure request-response demo message for UC97. Short, read-only worker-side work only; long work belongs to 202/status.
/// </summary>
public sealed record DemoQuickCheck(Guid UserId, int Input, int WorkDelayMs);

public sealed record DemoQuickCheckResult(int Score, string Reason);

public sealed class DemoQuickCheckHandler
{
    public async Task<DemoQuickCheckResult> Handle(DemoQuickCheck message, CancellationToken ct)
    {
        if (message.WorkDelayMs > 0)
        {
            await Task.Delay(message.WorkDelayMs, ct);
        }

        return new DemoQuickCheckResult(
            Score: Math.Clamp(message.Input * 2, 0, 100),
            Reason: "computed-in-worker");
    }
}
