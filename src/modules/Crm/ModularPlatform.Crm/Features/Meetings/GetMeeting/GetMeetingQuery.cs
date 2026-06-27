using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Meetings.GetMeeting;

public sealed record GetMeetingQuery(Guid UserId, Guid MeetingId)
    : IQuery<ModularPlatform.Crm.Features.Meetings.MeetingResponse>;
