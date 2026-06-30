using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Meetings.CreateMeeting;

/// <summary><paramref name="UserId"/> is the owner from the token (Law 10).</summary>
public sealed record CreateMeetingCommand(
    Guid UserId,
    Guid? ContactId,
    Guid? DealId,
    string Title,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string? Location,
    string? Notes) : ICommand<CreateMeetingResponse>;

public sealed record CreateMeetingResponse(Guid Id);

public sealed record CreateMeetingRequest(
    Guid? ContactId,
    Guid? DealId,
    string Title,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string? Location,
    string? Notes);
