using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Tasks.CreateTask;

/// <summary><paramref name="UserId"/> is the owner, set by the endpoint from the token — NEVER the request body (Law 10).</summary>
public sealed record CreateTaskCommand(
    Guid UserId,
    Guid? ContactId,
    Guid? DealId,
    string Title,
    string? Description,
    DateTimeOffset? DueAt,
    Guid? AssigneeUserId,
    string Priority) : ICommand<CreateTaskResponse>;

public sealed record CreateTaskResponse(Guid Id);

public sealed record CreateTaskRequest(
    Guid? ContactId,
    Guid? DealId,
    string Title,
    string? Description,
    DateTimeOffset? DueAt,
    Guid? AssigneeUserId,
    string? Priority);
