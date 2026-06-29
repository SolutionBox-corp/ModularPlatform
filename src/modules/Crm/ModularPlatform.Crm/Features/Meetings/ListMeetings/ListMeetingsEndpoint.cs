using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ModularPlatform.Abstractions;
using ModularPlatform.Crm.Features.Meetings;
using ModularPlatform.Cqrs;
using ModularPlatform.Web;

namespace ModularPlatform.Crm.Features.Meetings.ListMeetings;

internal static class ListMeetingsEndpoint
{
    public static void MapListMeetings(this IEndpointRouteBuilder app)
    {
        app.MapGet("/crm/meetings", async (
                DateTimeOffset? from,
                DateTimeOffset? to,
                Guid? contactId,
                string? status,
                int? page,
                int? pageSize,
                ITenantContext tenant,
                IDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var userId = tenant.UserId
                    ?? throw new UnauthorizedException("auth.required", "Authentication required.");
                var result = await dispatcher.Query(
                    new ListMeetingsQuery(userId, from, to, contactId, status, page, pageSize), ct);
                return Results.Ok(ApiResponse<PagedResponse<MeetingResponse>>.Ok(result));
            })
            .RequireAuthorization()
            .RequireModule("crm")
            .WithTags("Crm")
            .WithName("ListMeetings");
    }
}
