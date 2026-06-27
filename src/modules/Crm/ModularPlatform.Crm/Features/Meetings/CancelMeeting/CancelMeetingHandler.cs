using Microsoft.EntityFrameworkCore;
using ModularPlatform.Cqrs;
using ModularPlatform.Crm.Entities;
using ModularPlatform.Crm.Persistence;

namespace ModularPlatform.Crm.Features.Meetings.CancelMeeting;

/// <summary>Marks the caller's own meeting as canceled. Idempotent (re-cancel is a no-op). Foreign/missing ⇒ 404.</summary>
internal sealed class CancelMeetingHandler(CrmDbContext db)
    : ICommandHandler<CancelMeetingCommand, Unit>
{
    public async Task<Unit> Handle(CancelMeetingCommand command, CancellationToken ct)
    {
        var meeting = await db.Meetings
            .FirstOrDefaultAsync(m => m.Id == command.MeetingId && m.UserId == command.UserId, ct)
            ?? throw new NotFoundException("crm.meeting_not_found", "Meeting not found.");

        meeting.Status = MeetingStatuses.Canceled;
        await db.SaveChangesAsync(ct);

        return Unit.Value;
    }
}
