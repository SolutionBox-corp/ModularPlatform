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
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new StartDemoOperationCommand(userId), ct);
                return Results.Accepted($"/operations/{result.OperationId}", ApiResponse<StartDemoOperationResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Operations")
            .WithName("StartDemoOperation");
    }
}
