using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Meetings;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Meetings.GetMeeting;

internal static class GetMeetingEndpoint
{
    public static void MapGetMeeting(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/meetings/{meetingId:guid}", async (
                Guid meetingId,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(new GetMeetingQuery(userId, meetingId), ct);
                return Results.Ok(ApiResponse<MeetingResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("GetMeeting");
    }
}
