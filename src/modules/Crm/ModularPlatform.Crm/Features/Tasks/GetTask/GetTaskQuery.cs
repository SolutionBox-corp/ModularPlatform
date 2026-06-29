using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Tasks.GetTask;

public sealed record GetTaskQuery(Guid UserId, Guid TaskId)
    : IQuery<ModularPlatform.Crm.Features.Tasks.TaskResponse>;
