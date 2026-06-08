using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Persistence.Behaviors;

/// <summary>
/// Command-only. Retries a command up to 3× with exponential backoff (100/200/400ms) when it fails
/// with <see cref="DbUpdateConcurrencyException"/> (Postgres xmin conflict), reloading the conflicting
/// entries before each retry. Read queries never hit this.
/// </summary>
public sealed class ConcurrencyRetryBehavior<TRequest, TResponse>(
    ILogger<ConcurrencyRetryBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>, ICommandOnlyBehavior
{
    private const int MaxRetries = 3;
    private const int BaseDelayMs = 100;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await next();
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < MaxRetries)
            {
                attempt++;
                logger.LogWarning(
                    "Concurrency conflict in {Request}, retry {Attempt}/{Max}.",
                    typeof(TRequest).Name, attempt, MaxRetries);

                foreach (var entry in ex.Entries)
                {
                    await entry.ReloadAsync(ct);
                }

                await Task.Delay(BaseDelayMs * (int)Math.Pow(2, attempt - 1), ct);
            }
        }
    }
}
