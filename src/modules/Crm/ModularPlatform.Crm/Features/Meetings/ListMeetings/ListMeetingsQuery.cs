using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Meetings.ListMeetings;

/// <summary>Owner-scoped, paged meeting list with optional time window / contact / status filters. Soonest first.</summary>
public sealed record ListMeetingsQuery(
    Guid UserId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ContactId,
    Guid? CompanyId,
    string? Status,
    int? Page,
    int? PageSize) : IQuery<PagedResponse<ModularPlatform.Crm.Features.Meetings.MeetingResponse>>;
