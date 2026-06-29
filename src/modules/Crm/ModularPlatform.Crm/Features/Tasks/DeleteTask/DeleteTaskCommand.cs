using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Tasks.DeleteTask;

public sealed record DeleteTaskCommand(Guid UserId, Guid TaskId) : ICommand<Unit>;
