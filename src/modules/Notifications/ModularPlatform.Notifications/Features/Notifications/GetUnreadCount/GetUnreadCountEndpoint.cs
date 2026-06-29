using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Notifications.Features.Notifications.GetUnreadCount;

internal static class GetUnreadCountEndpoint
{
    public static void MapGetUnreadCount(this IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/me/unread-count", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetUnreadCountQuery(userId), ct);
                return Results.Ok(ApiResponse<UnreadCountResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("notifications")
            .WithTags("Notifications")
            .WithName("GetUnreadCount");
    }
}
