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
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var feed = await dispatcher.Query(
                    new GetMyNotificationsQuery(userId, unreadOnly ?? false, new PageRequest(page, pageSize)), ct);
                return Results.Ok(ApiResponse<PagedResponse<NotificationItem>>.Ok(feed));
            })
            .RequireAuthorization()
            .RequireModule("notifications")
            .WithTags("Notifications")
            .WithName("GetMyNotifications");
    }
}
