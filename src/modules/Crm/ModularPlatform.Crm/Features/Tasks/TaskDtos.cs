namespace ModularPlatform.Crm.Features.Tasks;

/// <summary>Shared read DTOs for the Tasks feature.</summary>
public sealed record TaskResponse(
    Guid Id,
    Guid? ContactId,
    Guid? DealId,
    Guid? AssigneeUserId,
    string Title,
    string? Description,
    DateTimeOffset? DueAt,
    string Priority,
    string Status,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record TaskCommentResponse(
    Guid Id,
    Guid TaskId,
    string Body,
    DateTimeOffset CreatedAt);
