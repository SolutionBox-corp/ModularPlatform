using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Admin.RevokeUserSessions;

public sealed record RevokeUserSessionsCommand(Guid UserId) : ICommand<Unit>;
