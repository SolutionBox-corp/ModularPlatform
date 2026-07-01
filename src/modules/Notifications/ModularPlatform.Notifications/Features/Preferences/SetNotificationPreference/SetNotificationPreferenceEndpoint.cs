using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Notifications.Features.Preferences.SetNotificationPreference;

internal static class SetNotificationPreferenceEndpoint
{
    public static void MapSetNotificationPreference(this IEndpointRouteBuilder app)
    {
        app.MapPut("/notifications/me/preferences/{channel}", async (
                string channel,
                SetNotificationPreferenceRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(new SetNotificationPreferenceCommand(userId, channel, request.Enabled), ct);
                return Results.Ok(ApiResponse<SetNotificationPreferenceResponse>.Ok(result));
            })
            .RequireAuthorization()
            .WithTags("Notifications")
            .WithName("SetNotificationPreference");
    }
}
