using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.AssignRole;

public sealed record AssignRoleCommand(Guid UserId, string RoleName) : ICommand<Unit>;

public sealed record AssignRoleRequest(string Role);
