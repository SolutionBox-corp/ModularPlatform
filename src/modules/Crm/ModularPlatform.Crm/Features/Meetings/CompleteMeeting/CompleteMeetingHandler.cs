using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.CompleteMeeting;

/// <summary>
/// Marks the caller's own meeting as done and records its outcome. If the meeting is linked to a contact, it also
/// drops a "meeting" interaction onto that contact's timeline (entity + log in one transaction). Foreign/missing ⇒ 404.
/// </summary>
internal sealed class CompleteMeetingHandler(CrmDbContext db)
    : ICommandHandler<CompleteMeetingCommand, Unit>
{
    public async Task<Unit> Handle(CompleteMeetingCommand command, CancellationToken ct)
    {
        var meeting = await db.Meetings
            .FirstOrDefaultAsync(m => m.Id == command.MeetingId && m.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.meeting_not_found", "Meeting not found.");

        var outcome = string.IsNullOrWhiteSpace(command.Outcome) ? null : command.Outcome;
        meeting.Status = MeetingStatuses.Done;
        meeting.Outcome = outcome;

        if (meeting.ContactId is { } contactId)
        {
            db.ContactInteractions.Add(new ContactInteraction
            {
                UserId = command.UserId,
                ContactId = contactId,
                Type = InteractionTypes.Meeting,
                OccurredAt = meeting.ScheduledAt,
                Body = outcome ?? meeting.Title,
            });
        }

        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
