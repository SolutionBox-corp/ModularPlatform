namespace ModularPlatform.Crm.Features.Meetings;

/// <summary>Shared read DTOs for the Meetings feature.</summary>
public sealed record MeetingResponse(
    Guid Id,
    Guid? ContactId,
    string? ContactName,
    string Title,
    DateTimeOffset ScheduledAt,
    int DurationMinutes,
    string? Location,
    string? Notes,
    string Status,
    string? Outcome,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
