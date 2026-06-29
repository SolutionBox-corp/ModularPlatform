using ModularPlatform.Cqrs;

namespace ModularPlatform.Crm.Features.Kanban.DeleteCard;

public sealed record DeleteCardCommand(Guid UserId, Guid CardId) : ICommand<Unit>;
