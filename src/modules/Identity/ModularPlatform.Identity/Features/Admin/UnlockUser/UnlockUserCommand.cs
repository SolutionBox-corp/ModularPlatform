using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.UnlockUser;

public sealed record UnlockUserCommand(Guid UserId) : ICommand<Unit>;
