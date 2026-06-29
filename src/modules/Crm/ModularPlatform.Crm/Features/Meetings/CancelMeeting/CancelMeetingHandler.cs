using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.CancelMeeting;

/// <summary>Marks the caller's own meeting as canceled. Idempotent (re-cancel is a no-op); a done meeting can't be canceled. Foreign/missing ⇒ 404.</summary>
internal sealed class CancelMeetingHandler(CrmDbContext db)
    : ICommandHandler<CancelMeetingCommand, Unit>
{
    public async Task<Unit> Handle(CancelMeetingCommand command, CancellationToken ct)
    {
        var meeting = await db.Meetings
            .FirstOrDefaultAsync(m => m.Id == command.MeetingId && m.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.meeting_not_found", "Meeting not found.");

        if (meeting.Status == MeetingStatuses.Canceled)
        {
            return Unit.Value;
        }

        if (meeting.Status != MeetingStatuses.Planned)
        {
            throw new BusinessRuleException(
                "crm.meeting.invalid_transition", "Only a planned meeting can be canceled.");
        }

        meeting.Status = MeetingStatuses.Canceled;
        meeting.Outcome = null;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
