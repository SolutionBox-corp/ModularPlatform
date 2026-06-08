using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Persistence.Behaviors;

/// <summary>
/// Command-only. Retries a command up to 5× with exponential backoff on a Postgres xmin
/// <see cref="DbUpdateConcurrencyException"/>. Before each retry it CLEARS the DbContext change tracker so the
/// handler re-runs against a fully fresh view (reloading only the conflicting entries would leave sibling
/// entities — e.g. an already-resolved hold — stale, causing the retry to repeat the same losing write).
/// Read queries never hit this.
/// </summary>
public sealed class ConcurrencyRetryBehavior<TRequest, TResponse>(
    ILogger<ConcurrencyRetryBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>, ICommandOnlyBehavior
{
    private const int MaxRetries = 5;
    private const int BaseDelayMs = 50;

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

                // Detach everything so the re-run re-queries fresh (sees the winning writer's committed state).
                ex.Entries.FirstOrDefault()?.Context.ChangeTracker.Clear();

                await Task.Delay(BaseDelayMs * (int)Math.Pow(2, attempt - 1), ct);
            }
        }
    }
}
