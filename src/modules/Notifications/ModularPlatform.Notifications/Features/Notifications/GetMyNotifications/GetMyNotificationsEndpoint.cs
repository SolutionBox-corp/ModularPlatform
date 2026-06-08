using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Notifications.Features.Notifications.GetMyNotifications;

internal static class GetMyNotificationsEndpoint
{
    public static void MapGetMyNotifications(this IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/me", async (
                bool? unreadOnly,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var feed = await dispatcher.Query(new GetMyNotificationsQuery(userId, unreadOnly ?? false), ct);
                return Results.Ok(ApiResponse<IReadOnlyList<NotificationItem>>.Ok(feed));
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("GetMyNotifications");
    }
}
