using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Meetings.CreateMeeting;

internal static class CreateMeetingEndpoint
{
    public static void MapCreateMeeting(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/meetings", async (
                CreateMeetingRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                LinkGenerator links,
                HttpContext http,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Send(
                    new CreateMeetingCommand(
                        userId,
                        request.ContactId,
                        request.DealId,
                        request.Title ?? string.Empty,
                        request.ScheduledAt,
                        request.DurationMinutes,
                        request.Location,
                        request.Notes),
                    ct);
                var location = links.GetPathByName(http, "GetMeeting", new { meetingId = result.Id })
                    ?? $"/crm/meetings/{result.Id}";
                return Results.Created(location, ApiResponse<CreateMeetingResponse>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CreateMeeting");
    }
}
