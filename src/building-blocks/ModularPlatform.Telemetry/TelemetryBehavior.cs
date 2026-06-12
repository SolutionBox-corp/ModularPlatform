using System.Diagnostics;
using ModularPlatform.Cqrs;

namespace ModularPlatform.Telemetry;

/// <summary>
/// Outer-most behavior: opens an OpenTelemetry activity span per command/query, tagged with the
/// request type and outcome. Registered FIRST in the host so it wraps everything else.
/// </summary>
public sealed class TelemetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        using var activity = PlatformTelemetry.Source.StartActivity($"cqrs {typeof(TRequest).Name}");
        activity?.SetTag("cqrs.request", typeof(TRequest).Name);
        try
        {
            var response = await next();
            activity?.SetStatus(ActivityStatusCode.Ok);
            return response;
        }
        catch (OperationCanceledException)
        {
            // Client disconnect / shutdown cancellation is not a server error — don't inflate error-rate SLOs.
            activity?.SetStatus(ActivityStatusCode.Ok);
            throw;
        }
        catch (ModularPlatformException ex)
        {
            activity?.SetTag("cqrs.error_code", ex.ErrorCode);
            activity?.SetStatus(ActivityStatusCode.Error, ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}

public static class PlatformTelemetry
{
    public const string SourceName = "ModularPlatform";
    public static readonly ActivitySource Source = new(SourceName);
}
