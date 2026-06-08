using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ModularPlatform.Cqrs.Behaviors;

/// <summary>
/// Structured request/response/error log around every command and query, with elapsed time.
/// Intentionally does not log the request body (PII); only the type name.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var requestName = typeof(TRequest).Name;
        var start = Stopwatch.GetTimestamp();
        try
        {
            var response = await next();
            logger.LogInformation("Handled {Request} in {Elapsed}ms",
                requestName, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            return response;
        }
        catch (ModularPlatformException ex)
        {
            // Expected business errors -> warning, no stack noise.
            logger.LogWarning("{Request} rejected with {ErrorCode} after {Elapsed}ms: {Message}",
                requestName, ex.ErrorCode, Stopwatch.GetElapsedTime(start).TotalMilliseconds, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Request} failed after {Elapsed}ms",
                requestName, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            throw;
        }
    }
}
