using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.CreateMeeting;

/// <summary>
/// Schedules a meeting for the caller. If a contact is referenced it must be owned (foreign/missing ⇒ 404).
/// Status starts "planned". Write without an event (no other module reacts yet). UTC times throughout.
/// </summary>
internal sealed class CreateMeetingHandler(CrmDbContext db)
    : ICommandHandler<CreateMeetingCommand, CreateMeetingResponse>
{
    public async Task<CreateMeetingResponse> Handle(CreateMeetingCommand command, CancellationToken ct)
    {
        if (command.ContactId is { } contactId)
        {
            var owned = await db.Contacts.AnyAsync(c => c.Id == contactId && c.UserId == command.UserId, ct);
            if (!owned)
            {
                throw new NotFoundException("crm.contact_not_found", "Contact not found.");
            }
        }

        var meeting = new Meeting
        {
            UserId = command.UserId,
            ContactId = command.ContactId,
            Title = command.Title.Trim(),
            ScheduledAt = command.ScheduledAt.ToUniversalTime(),
            DurationMinutes = command.DurationMinutes,
            Location = string.IsNullOrWhiteSpace(command.Location) ? null : command.Location.Trim(),
            Notes = string.IsNullOrWhiteSpace(command.Notes) ? null : command.Notes,
            Status = MeetingStatuses.Planned,
        };

        db.Meetings.Add(meeting);
        await db.SaveChangesAsync(ct);

        return new CreateMeetingResponse(meeting.Id);
    }
}
