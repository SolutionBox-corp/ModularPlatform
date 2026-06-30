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

        var row = await db.Meetings
            .Where(m => m.Id == query.MeetingId && m.UserId == query.UserId)
            .Select(m => new
            {
                m.Id,
                m.ContactId,
                ContactFirstName = db.Contacts
                    .Where(c => c.Id == m.ContactId && c.UserId == query.UserId)
                    .Select(c => c.FirstName)
                    .FirstOrDefault(),
                ContactLastName = db.Contacts
                    .Where(c => c.Id == m.ContactId && c.UserId == query.UserId)
                    .Select(c => c.LastName)
                    .FirstOrDefault(),
                m.Title,
                m.ScheduledAt,
                m.DurationMinutes,
                m.Location,
                m.Notes,
                m.Status,
                m.Outcome,
                m.CreatedAt,
                m.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? throw new NotFoundException("crm.meeting_not_found", "Meeting not found.")
            : new MeetingResponse(
                row.Id,
                row.ContactId,
                FormatContactName(row.ContactFirstName, row.ContactLastName),
                row.Title,
                row.ScheduledAt,
                row.DurationMinutes,
                row.Location,
                row.Notes,
                row.Status,
                row.Outcome,
                row.CreatedAt,
                row.UpdatedAt);
    }

    private static string? FormatContactName(string? firstName, string? lastName)
    {
        var name = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
