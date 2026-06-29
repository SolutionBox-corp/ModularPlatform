using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Tasks.UpdateTask;

/// <summary>Partial patch — a null field is unchanged. Completion goes through CompleteTask, not here.</summary>
public sealed record UpdateTaskCommand(
    Guid UserId,
    Guid TaskId,
    string? Title,
    string? Description,
    DateTimeOffset? DueAt,
    string? Priority) : ICommand<ModularPlatform.Crm.Features.Tasks.TaskResponse>;

public sealed record UpdateTaskRequest(
    string? Title,
    string? Description,
    DateTimeOffset? DueAt,
    string? Priority);
