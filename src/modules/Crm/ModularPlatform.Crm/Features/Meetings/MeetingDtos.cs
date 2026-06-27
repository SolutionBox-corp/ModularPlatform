namespace ModularPlatform.Crm.Features.Meetings;

/// <summary>Shared read DTOs for the Meetings feature.</summary>
public sealed record MeetingResponse(
    Guid Id,
    Guid? ContactId,
    string Title,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string? Location,
    string? Notes,
    string Status,
    string? Outcome,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record MeetingsPageResponse(
    IReadOnlyList<MeetingResponse> Items,
    int Total,
    int Limit,
    int Offset);
