using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Meetings;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Meetings.UpdateMeeting;

internal static class UpdateMeetingEndpoint
{
    public static void MapUpdateMeeting(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/crm/meetings/{meetingId:guid}", async (
                Guid meetingId,
                UpdateMeetingRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new UpdateMeetingCommand(
                        userId,
                        meetingId,
                        request.Title ?? string.Empty,
                        request.ScheduledAt,
                        request.DurationMinutes,
                        request.Location,
                        request.Notes),
                    ct);
                return Results.Ok(ApiResponse<MeetingResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("UpdateMeeting");
    }
}
