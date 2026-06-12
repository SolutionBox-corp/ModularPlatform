using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Operations.Features.Demo;

/// <summary>
/// CANONICAL 202 endpoint: accepts the request, kicks off durable work, and returns <c>202 Accepted</c> with a
/// <c>Location</c> pointing at the status endpoint. The owner is taken from the token — never from the body.
/// </summary>
internal static class StartDemoOperationEndpoint
{
    public static void MapStartDemoOperation(this IEndpointRouteBuilder app)
    {
        app.MapPost("/operations/demo", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new StartDemoOperationCommand(userId), ct);
                // Build the status Location from the named status route so it stays correct under any route-group
                // prefix (e.g. the host's /v1 versioning group) instead of hardcoding the path.
                var location = links.GetPathByName(http, "GetOperationStatus", new { operationId = result.OperationId })
                    ?? $"/operations/{result.OperationId}";
                return Results.Accepted(location, ApiResponse<StartDemoOperationResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("operations")
            .WithTags("Operations")
            .WithName("StartDemoOperation");
    }
}
