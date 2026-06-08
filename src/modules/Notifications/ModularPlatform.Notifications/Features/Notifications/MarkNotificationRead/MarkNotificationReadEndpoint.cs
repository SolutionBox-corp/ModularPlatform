using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Notifications.Features.Notifications.MarkNotificationRead;

internal static class MarkNotificationReadEndpoint
{
    public static void MapMarkNotificationRead(this IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/{notificationId:guid}/read", async (
                Guid notificationId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new MarkNotificationReadCommand(userId, notificationId), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("MarkNotificationRead");
    }
}
