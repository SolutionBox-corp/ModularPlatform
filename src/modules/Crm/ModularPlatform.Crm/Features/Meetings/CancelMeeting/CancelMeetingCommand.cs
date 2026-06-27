using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Meetings.CancelMeeting;

public sealed record CancelMeetingCommand(Guid UserId, Guid MeetingId) : ICommand<Unit>;
