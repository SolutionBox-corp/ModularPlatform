using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Meetings.CompleteMeeting;

public sealed record CompleteMeetingCommand(Guid UserId, Guid MeetingId, string? Outcome) : ICommand<Unit>;

public sealed record CompleteMeetingRequest(string? Outcome);
