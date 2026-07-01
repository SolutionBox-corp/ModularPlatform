using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Notifications.Features.Preferences.GetMyNotificationPreferences;

internal static class GetMyNotificationPreferencesEndpoint
{
    public static void MapGetMyNotificationPreferences(this IEndpointRouteBuilder app)
    {
        app.MapGet("/notifications/me/preferences", async (
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetMyNotificationPreferencesQuery(userId), ct);
                return Results.Ok(ApiResponse<GetMyNotificationPreferencesResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("GetMyNotificationPreferences");
    }
}
