using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Meetings.ListMeetings;

/// <summary>Owner-scoped, paged meeting list with optional time window / contact / status filters. Soonest first.</summary>
public sealed record ListMeetingsQuery(
    Guid UserId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ContactId,
    string? Status,
    int Limit,
    int Offset) : IQuery<ModularPlatform.Crm.Features.Meetings.MeetingsPageResponse>;
