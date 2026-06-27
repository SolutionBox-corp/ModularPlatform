using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Features.Meetings;
using ModularPlatform.Crm.Persistence;
using ModularPlatform.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.GetMeeting;

internal sealed class GetMeetingHandler(IReadDbContextFactory<CrmDbContext> readFactory)
    : IQueryHandler<GetMeetingQuery, MeetingResponse>
{
    public async Task<MeetingResponse> Handle(GetMeetingQuery query, CancellationToken ct)
    {
        await using var db = readFactory.Create();

        var meeting = await db.Meetings
            .Where(m => m.Id == query.MeetingId && m.UserId == query.UserId)
            .Select(m => new MeetingResponse(
                m.Id, m.ContactId, m.Title, m.ScheduledAt, m.DurationMinutes, m.Location, m.Notes,
                m.Status, m.Outcome, m.CreatedAt, m.UpdatedAt))
            .FirstOrDefaultAsync(ct);

        return meeting ?? throw new NotFoundException("crm.meeting_not_found", "Meeting not found.");
    }
}
