using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Operations.Features.DemoInvoke;

internal static class InvokeDemoCheckEndpoint
{
    public static void MapInvokeDemoCheck(this IEndpointRouteBuilder app)
    {
        app.MapPost("/operations/demo-invoke", async (
                InvokeDemoCheckRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new InvokeDemoCheckCommand(
                    userId,
                    request.Input,
                    request.TimeoutMs ?? 3_000,
                    request.WorkDelayMs ?? 0), ct);
                return Results.Ok(ApiResponse<InvokeDemoCheckResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("operations")
            .WithTags("Operations")
            .WithName("InvokeDemoCheck");
    }
}
