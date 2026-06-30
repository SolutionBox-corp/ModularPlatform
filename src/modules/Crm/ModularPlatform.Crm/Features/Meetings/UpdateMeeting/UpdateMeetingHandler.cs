using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Features.Meetings;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.UpdateMeeting;

/// <summary>Reschedules/edits the caller's own meeting (title/time/duration/location/notes). Status is unchanged.</summary>
internal sealed class UpdateMeetingHandler(CrmDbContext db)
    : ICommandHandler<UpdateMeetingCommand, MeetingResponse>
{
    public async Task<MeetingResponse> Handle(UpdateMeetingCommand command, CancellationToken ct)
    {
        var meeting = await db.Meetings
            .FirstOrDefaultAsync(m => m.Id == command.MeetingId && m.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.meeting_not_found", "Meeting not found.");

        if (meeting.Status != MeetingStatuses.Planned)
        {
            throw new BusinessRuleException(
                "crm.meeting.invalid_transition", "Only a planned meeting can be rescheduled.");
        }

        meeting.Title = command.Title.Trim();
        meeting.ScheduledAt = command.ScheduledAt.ToUniversalTime();
        meeting.DurationMinutes = command.DurationMinutes;
        meeting.Location = string.IsNullOrWhiteSpace(command.Location) ? null : command.Location.Trim();
        meeting.Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes;

        await db.SaveChangesAsync(ct);

        var contact = meeting.ContactId is { } contactId
            ? await db.Contacts
                .Where(c => c.Id == contactId && c.UserId == command.UserId)
                .Select(c => new { c.FirstName, c.LastName })
                .FirstOrDefaultAsync(ct)
            : null;

        return new MeetingResponse(
            meeting.Id, meeting.ContactId, FormatContactName(contact?.FirstName, contact?.LastName), meeting.Title, meeting.ScheduledAt, meeting.DurationMinutes,
            meeting.Location, meeting.Notes, meeting.Status, meeting.Outcome, meeting.CreatedAt, meeting.UpdatedAt);
    }

    private static string? FormatContactName(string? firstName, string? lastName)
    {
        var name = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }
}
