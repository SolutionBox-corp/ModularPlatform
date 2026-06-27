using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Meetings.UpdateMeeting;

public sealed record UpdateMeetingCommand(
    Guid UserId,
    Guid MeetingId,
    string Title,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string? Location,
    string? Notes) : ICommand<ModularPlatform.Crm.Features.Meetings.MeetingResponse>;

public sealed record UpdateMeetingRequest(
    string Title,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string? Location,
    string? Notes);
