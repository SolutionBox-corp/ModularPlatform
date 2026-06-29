using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Tasks.CompleteTask;

public sealed record CompleteTaskCommand(Guid UserId, Guid TaskId) : ICommand<Unit>;
