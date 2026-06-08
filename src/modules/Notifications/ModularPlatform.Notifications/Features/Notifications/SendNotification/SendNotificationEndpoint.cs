using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("SendNotification");
    }
}
