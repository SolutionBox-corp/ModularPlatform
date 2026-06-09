using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.RevokeRole;

public sealed record RevokeRoleCommand(Guid UserId, string RoleName) : ICommand<Unit>;
