using ModularPlatform.Cqrs;

namespace ModularPlatform.Identity.Features.Auth.Logout;

public sealed record LogoutCommand(Guid UserId, string RefreshToken) : ICommand<Unit>;

public sealed record LogoutRequest(string RefreshToken);
