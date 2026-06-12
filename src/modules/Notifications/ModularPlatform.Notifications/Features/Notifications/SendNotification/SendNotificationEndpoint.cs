using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Notifications.Features.Notifications.SendNotification;

internal static class SendNotificationEndpoint
{
    public static void MapSendNotification(this IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/send", async (
                SendNotificationRequest request,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                await dispatcher.Send(new SendNotificationCommand(
                    request.UserId, request.TemplateKey, request.Channels, request.Data), ct);
                return Results.Ok(ApiResponse<Unit>.Ok(Unit.Value));
            })
            // Sending to an arbitrary UserId is a system/admin operation — gate it on a permission so a normal
            // authenticated user can't push notifications to others. System/worker sends bypass HTTP entirely.
            .RequirePermission(PlatformPermissions.NotificationsSend)
            .RequireModule("notifications")
            .WithTags("Notifications")
            .WithName("SendNotification");
    }
}
