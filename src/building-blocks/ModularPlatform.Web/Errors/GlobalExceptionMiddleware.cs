using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ModularPlatform.Cqrs;
using ModularPlatform.Web.Localization;

namespace ModularPlatform.Web.Errors;

/// <summary>
/// Translates every unhandled exception into an RFC 9457 Problem Details response.
/// <para>
/// - <c>type</c>/<c>title</c> are stable + language-independent (keyed by errorCode) so clients branch on the code.
/// - <c>detail</c> is localized from the resx whose key == errorCode (Accept-Language negotiated).
/// - validation failures add an <c>errors[]</c> extension of { field, errorCode, message }.
/// </para>
/// This is THE error contract. Handlers throw <see cref="ModularPlatformException"/> subclasses;
/// they never build HTTP responses themselves.
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    IStringLocalizer<SharedResource> localizer,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironmentAccessor env)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ModularPlatformException ex)
        {
            await WriteProblem(context, ex.StatusCode, ex.ErrorCode, ex, (ex as ValidationException)?.Errors);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception");
            await WriteProblem(context, StatusCodes.Status500InternalServerError, "error.unexpected", ex, null);
        }
    }

    private async Task WriteProblem(
        HttpContext context, int status, string errorCode, Exception ex, IReadOnlyList<ValidationError>? errors)
    {
        var detail = localizer[errorCode];
        // NEVER fall back to ex.Message — a developer string can carry internal detail (DB text, hostnames, ids). An
        // errorCode with no resx entry (a forgotten translation) degrades to a safe generic, not a leak. (The resx
        // parity test should keep this branch unreachable in practice; this is the defence if one slips through.)
        var safeDetail = detail.ResourceNotFound ? localizer["error.unexpected"].Value : detail.Value;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = errorCode,
            Type = $"https://errors.modularplatform.dev/{errorCode}",
            Detail = safeDetail,
        };
        problem.Extensions["errorCode"] = errorCode;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        if (errors is { Count: > 0 })
        {
            problem.Extensions["errors"] = errors.Select(e => new
            {
                field = e.Field,
                errorCode = e.ErrorCode,
                message = e.Message,
            });
        }

        if (env.IsDevelopment && status == StatusCodes.Status500InternalServerError)
        {
            problem.Extensions["exception"] = ex.ToString();
        }

        // If the response already began streaming (e.g. an exception thrown mid-SSE after headers flushed), we can no
        // longer write a problem body — overwriting the status would throw. Log and bail rather than mask the original.
        if (context.Response.HasStarted)
        {
            logger.LogWarning(ex, "Cannot write a problem response for {ErrorCode}: the response already started.", errorCode);
            return;
        }

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }
}

/// <summary>Tiny abstraction so the middleware needn't reference the hosting env type directly.</summary>
public interface IHostEnvironmentAccessor
{
    bool IsDevelopment { get; }
}
