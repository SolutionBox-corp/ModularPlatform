using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Meetings.CompleteMeeting;

internal static class CompleteMeetingEndpoint
{
    public static void MapCompleteMeeting(this IEndpointRouteBuilder app)
    {
        app.MapPost("/crm/meetings/{meetingId:guid}/complete", async (
                Guid meetingId,
                CompleteMeetingRequest request,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                await dispatcher.Send(new CompleteMeetingCommand(userId, meetingId, request.Outcome), ct);
                return Results.NoContent();
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("CompleteMeeting");
    }
}
