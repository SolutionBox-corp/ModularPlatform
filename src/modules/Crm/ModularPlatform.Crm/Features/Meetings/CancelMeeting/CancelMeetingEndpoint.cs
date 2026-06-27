using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Meetings.CancelMeeting;

internal static class CancelMeetingEndpoint
{
    public static void MapCancelMeeting(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/meetings/{meetingId:guid}/cancel", async (
                Guid meetingId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new CancelMeetingCommand(userId, meetingId), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CancelMeeting");
    }
}
