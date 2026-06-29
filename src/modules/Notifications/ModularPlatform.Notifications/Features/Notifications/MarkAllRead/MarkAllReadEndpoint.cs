using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Notifications.Features.Notifications.MarkAllRead;

internal static class MarkAllReadEndpoint
{
    public static void MapMarkAllRead(this IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/me/read-all", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new MarkAllReadCommand(userId), ct);
                return Results.Ok(ApiResponse<MarkAllReadResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("notifications")
            .WithTags("Notifications")
            .WithName("MarkAllRead");
    }
}
