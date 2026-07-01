using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Tasks.AddTaskComment;

public sealed record AddTaskCommentCommand(
    Guid UserId,
    Guid TaskId,
    string Body) : ICommand<AddTaskCommentResponse>;

public sealed record AddTaskCommentResponse(Guid Id);

public sealed record AddTaskCommentRequest(string Body);
