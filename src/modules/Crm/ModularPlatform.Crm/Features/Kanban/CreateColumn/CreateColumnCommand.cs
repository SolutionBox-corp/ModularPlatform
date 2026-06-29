using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.CreateColumn;

public sealed record CreateColumnCommand(Guid UserId, Guid BoardId, string Name) : ICommand<CreateColumnResponse>;
public sealed record CreateColumnResponse(Guid Id);
public sealed record CreateColumnRequest(string Name);
